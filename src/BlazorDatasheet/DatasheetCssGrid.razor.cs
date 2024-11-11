﻿using BlazorDatasheet.Core.Commands.Data;
using BlazorDatasheet.Core.Data;
using BlazorDatasheet.Core.Edit;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.Core.Util;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.Edit;
using BlazorDatasheet.Events;
using BlazorDatasheet.Extensions;
using BlazorDatasheet.KeyboardInput;
using BlazorDatasheet.Render;
using BlazorDatasheet.Render.DefaultComponents;
using BlazorDatasheet.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using ClipboardEventArgs = BlazorDatasheet.Core.Events.ClipboardEventArgs;
using Microsoft.JSInterop;

namespace BlazorDatasheet;

public partial class DatasheetCssGrid : SheetComponentBase
{
    [Inject] private IJSRuntime Js { get; set; } = null!;
    [Inject] private IWindowEventService WindowEventService { get; set; } = null!;
    [Inject] private IMenuService MenuService { get; set; } = null!;
    private IClipboard ClipboardService { get; set; } = null!;

    /// <summary>
    /// The Sheet holding the data for the datasheet.
    /// </summary>
    [Parameter, EditorRequired]
    public Sheet? Sheet { get; set; }

    private Sheet _sheet = new(1, 1);

    /// <summary>
    /// When set, this restricts the datasheet to viewing this region, otherwise the datasheet views the whole sheet.
    /// </summary>
    [Parameter]
    public Region? ViewRegion { get; set; } = null;

    private Region _viewRegion = new(0, 0);

    /// <summary>
    /// Datasheet theme that controls the css variables used to style the sheet.
    /// </summary>
    [Parameter]
    public string Theme { get; set; } = "default";

    /// <summary>
    /// Renders graphics that show which cell formulas are dependent on others.
    /// </summary>
    [Parameter]
    public bool ShowFormulaDependents { get; set; }

    /// <summary>
    /// Fired when the Datasheet becomes active or inactive (able to receive keyboard inputs).
    /// </summary>
    [Parameter]
    public EventCallback<SheetActiveEventArgs> OnSheetActiveChanged { get; set; }

    /// <summary>
    /// Set to true when the datasheet should not be edited
    /// </summary>
    [Parameter]
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether to show the row headings.
    /// </summary>
    [Parameter]
    public bool ShowRowHeadings { get; set; } = true;

    /// <summary>
    /// Whether to show the column headings.
    /// </summary>
    [Parameter]
    public bool ShowColHeadings { get; set; } = true;

    /// <summary>
    /// Specifies how many columns are frozen on the left side of the grid.
    /// </summary>
    [Parameter]
    public int FrozenLeftCount { get; set; }

    private int _frozenLeftCount;

    /// <summary>
    /// Specifies how many columns are frozen on the right side of the grid.
    /// </summary>
    [Parameter]
    public int FrozenRightCount { get; set; }

    private int _frozenRightCount;

    /// <summary>
    /// Specifies how many rows are frozen on the top side of the grid.
    /// </summary>
    [Parameter]
    public int FrozenTopCount { get; set; }

    private int _frozenTopCount;

    /// <summary>
    /// Specifies how many rows are frozen on the bottom side of the grid.
    /// </summary>
    [Parameter]
    public int FrozenBottomCount { get; set; }

    private int _frozenBottomCount;

    /// <summary>
    /// An indicator of how deep the grid is. Any sub-grid of the grid should have a higher <see cref="GridLevel"/> than its parent.
    /// This is used internally and should not be used in most circumstances.
    /// </summary>
    [Parameter]
    public int GridLevel { get; set; }

    /// <summary>
    /// Register custom editor components (derived from <see cref="BaseEditor"/>) that will be selected
    /// based on the cell type.
    /// </summary>
    [Parameter]
    public Dictionary<string, CellTypeDefinition> CustomCellTypeDefinitions { get; set; } = new();

    /// <summary>
    /// Supplies a dictionary of <seealso cref="RenderFragment"/> items that represent various icons.
    /// </summary>
    [Parameter]
    public Dictionary<string, RenderFragment> Icons { get; set; } = new();

    /// <summary>
    /// When set to true (default), the sheet will be virtualised, meaning only the visible cells will be rendered.
    /// </summary>
    [Parameter]
    public bool Virtualise { get; set; } = true;

    /// <summary>
    /// The datasheet keyboard shortcut manager
    /// </summary>
    public ShortcutManager ShortcutManager { get; } = new();

    /// <summary>
    /// Whether the user is focused on the datasheet.
    /// </summary>
    private bool IsDataSheetActive { get; set; }

    /// <summary>
    /// Whether the mouse is located inside/over the sheet.
    /// </summary>
    private bool IsMouseInsideSheet { get; set; }

    [Parameter] public bool StickyHeaders { get; set; } = true;

    private DotNetObjectReference<DatasheetCssGrid> _dotnetHelper = default!;

    private SheetPointerInputService _sheetPointerInputService = null!;

    /// <summary>
    /// The whole sheet container, useful for checking whether mouse is inside the sheet
    /// </summary>
    private ElementReference _sheetContainer = default!;
    
    /// <summary>
    /// The editor layer, which renders the cell editor.
    /// </summary>
    private EditorLayer _editorLayer = default!;

    /// <summary>
    /// The size of the main region of this datasheet, that is the region of the grid without
    /// any frozen rows or columns.
    /// </summary>
    private Region MainViewRegion => new(
        Math.Max(FrozenTopCount, _viewRegion.Top),
        Math.Min(_viewRegion.Bottom - _frozenBottomCount, _viewRegion.Bottom),
        Math.Max(FrozenLeftCount, _viewRegion.Left),
        Math.Min(_viewRegion.Right - _frozenRightCount, _viewRegion.Right));

    protected override void OnInitialized()
    {
        ClipboardService = new Clipboard(Js);
        RegisterDefaultShortcuts();
        base.OnInitialized();
    }

    protected override Task OnParametersSetAsync()
    {
        if (Sheet != _sheet)
        {
            RemoveEvents(_sheet);
            _sheet = Sheet ?? new(0, 0);
            AddEvents(_sheet);
        }

        if (ViewRegion != _viewRegion)
        {
            _viewRegion = ViewRegion ?? _sheet.Region;
        }

        if (_frozenLeftCount != FrozenLeftCount || _frozenRightCount != FrozenRightCount)
        {
            _frozenLeftCount = FrozenLeftCount;
            _frozenRightCount = FrozenRightCount;
            _frozenBottomCount = FrozenBottomCount;
            _frozenTopCount = FrozenTopCount;
        }

        StateHasChanged();

        return base.OnParametersSetAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            if (GridLevel > 0)
                return;

            _dotnetHelper = DotNetObjectReference.Create(this);

            _sheetPointerInputService = new SheetPointerInputService(Js, _sheetContainer);
            await _sheetPointerInputService.Init();

            _sheetPointerInputService.PointerDown += this.HandleCellMouseDown;
            _sheetPointerInputService.PointerEnter += HandleCellMouseOver;
            _sheetPointerInputService.PointerDoubleClick += HandleCellDoubleClick;

            await AddWindowEventsAsync();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private void RemoveEvents(Sheet sheet)
    {
    }

    private void AddEvents(Sheet sheet)
    {
        _sheet.Editor.EditBegin += async (_, _) => await WindowEventService.CancelPreventDefault("keydown");
        _sheet.Editor.EditFinished +=
            async (_, _) => await WindowEventService.PreventDefault("keydown");
    }

    private async Task AddWindowEventsAsync()
    {
        await WindowEventService.RegisterMouseEvent("mousedown", HandleWindowMouseDown);
        await WindowEventService.RegisterKeyEvent("keydown", HandleWindowKeyDown);
        await WindowEventService.RegisterClipboardEvent("paste", HandleWindowPaste);
        await WindowEventService.RegisterMouseEvent("mouseup", HandleWindowMouseUp);
    }

    internal async Task<bool> HandleShortcuts(string key, KeyboardModifiers modifiers)
    {
        return await ShortcutManager.ExecuteAsync(key, modifiers,
            new ShortcutExecutionContext(this._sheet));
    }

    private void RegisterDefaultShortcuts()
    {
        ShortcutManager.Register(["Escape"], KeyboardModifiers.Any,
            _ => _sheet.Editor.CancelEdit());

        ShortcutManager
            .Register(["Enter"], KeyboardModifiers.None, _ => AcceptEditAndMoveActiveSelection(Axis.Row, 1));
        ShortcutManager
            .Register(["Enter"], KeyboardModifiers.Shift, _ => AcceptEditAndMoveActiveSelection(Axis.Row, -1));

        ShortcutManager
            .Register(["Tab"], KeyboardModifiers.None, _ => AcceptEditAndMoveActiveSelection(Axis.Col, 1));
        ShortcutManager
            .Register(["Tab"], KeyboardModifiers.Shift, _ => AcceptEditAndMoveActiveSelection(Axis.Col, -1));

        ShortcutManager
            .Register(["KeyC"], [KeyboardModifiers.Ctrl, KeyboardModifiers.Meta],
                async (_) => await CopySelectionToClipboard(),
                _ => !_sheet.Editor.IsEditing);

        ShortcutManager
            .Register(["ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft"], KeyboardModifiers.None,
                c =>
                    HandleArrowKeysDown(false, KeyUtil.GetMovementFromArrowKey(c.Key)));

        ShortcutManager
            .Register(["ArrowUp", "ArrowRight", "ArrowDown", "ArrowLeft"], KeyboardModifiers.Shift,
                c =>
                    HandleArrowKeysDown(true, KeyUtil.GetMovementFromArrowKey(c.Key)));

        ShortcutManager.Register(["KeyY"], [KeyboardModifiers.Ctrl, KeyboardModifiers.Meta],
            _ => _sheet.Commands.Redo(),
            _ => !_sheet.Editor.IsEditing);
        ShortcutManager.Register(["KeyZ"], [KeyboardModifiers.Ctrl, KeyboardModifiers.Meta],
            _ => _sheet.Commands.Undo(),
            _ => !_sheet.Editor.IsEditing);

        ShortcutManager.Register(["Delete", "Backspace"], KeyboardModifiers.Any,
            _ => _sheet.Commands.ExecuteCommand(new ClearCellsCommand(_sheet.Selection.Regions)),
            _ => _sheet.Selection.Regions.Any() && !_sheet.Editor.IsEditing);
    }

    private void HandleCellMouseDown(object? sender, SheetPointerEventArgs args)
    {
        // if rmc and inside a selection, don't do anything
        if (args.MouseButton == 2 && _sheet.Selection.Contains(args.Row, args.Col))
            return;


        if (_sheet.Editor.IsEditing)
        {
            if (!(_sheet.Editor.EditCell!.Row == args.Row && _sheet.Editor.EditCell!.Col == args.Col))
            {
                if (!_sheet.Editor.AcceptEdit())
                    return;
            }
        }

        if (args.ShiftKey && _sheet.Selection.ActiveRegion != null)
        {
            _sheet.Selection.ExtendTo(args.Row, args.Col);
        }
        else
        {
            if (!args.MetaKey && !args.CtrlKey)
            {
                _sheet.Selection.ClearSelections();
            }

            if (args.Row == -1)
                _sheet.Selection.BeginSelectingCol(args.Col);
            else if (args.Col == -1)
                _sheet.Selection.BeginSelectingRow(args.Row);
            else
                _sheet.Selection.BeginSelectingCell(args.Row, args.Col);

            if (args.MouseButton == 2) // RMC
                _sheet.Selection.EndSelecting();
        }
    }

    private async Task<bool> HandleWindowKeyDown(KeyboardEventArgs e)
    {
        if (!IsDataSheetActive)
            return false;

        if (MenuService.IsMenuOpen())
            return false;

        var editorHandled = _editorLayer.HandleKeyDown(e.Key, e.CtrlKey, e.ShiftKey, e.AltKey, e.MetaKey);
        if (editorHandled)
            return true;

        var modifiers = e.GetModifiers();
        if (await HandleShortcuts(e.Key, modifiers) || await HandleShortcuts(e.Code, modifiers))
            return true;

        // Single characters or numbers or symbols
        if ((e.Key.Length == 1) && !_sheet.Editor.IsEditing && IsDataSheetActive)
        {
            // Don't input anything if we are currently selecting
            if (_sheet.Selection.IsSelecting)
                return false;

            // Capture commands and return early (mainly for paste)
            if (e.CtrlKey || e.MetaKey)
                return false;

            char c = e.Key == "Space" ? ' ' : e.Key[0];
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsSeparator(c))
            {
                if (!_sheet.Selection.Regions.Any())
                    return false;
                var inputPosition = _sheet.Selection.GetInputPosition();

                await BeginEdit(inputPosition.row, inputPosition.col, EditEntryMode.Key, e.Key);
            }

            return true;
        }

        return false;
    }

    private async Task<bool> HandleWindowMouseDown(MouseEventArgs e)
    {
        bool changed = IsDataSheetActive != IsMouseInsideSheet;
        await SetActiveAsync(IsMouseInsideSheet);

        if (changed)
            StateHasChanged();

        return false;
    }

    private Task<bool> HandleWindowMouseUp(MouseEventArgs arg)
    {
        _sheet.Selection.EndSelecting();
        return Task.FromResult(false);
    }

    private async Task<bool> HandleArrowKeysDown(bool shift, Offset offset)
    {
        var accepted = true;
        if (_sheet.Editor.IsEditing)
            accepted = _sheet.Editor.IsSoftEdit && _sheet.Editor.AcceptEdit();

        if (!accepted) return false;

        if (shift)
        {
            var oldActiveRegion = _sheet.Selection.ActiveRegion?.Clone();
            GrowActiveSelection(offset);
            if (oldActiveRegion == null) return false;
            var r = _sheet.Selection.ActiveRegion!.Break(oldActiveRegion).FirstOrDefault();

            if (r == null)
            {
                // if r is null we are instead shrinking the region, so instead break the old region with the new
                // but we contract the new region to ensure that it is now visible
                var rNew = _sheet.Selection.ActiveRegion.Clone();
                Edge edge = Edge.None;
                if (offset.Rows == 1)
                    edge = Edge.Top;
                else if (offset.Rows == -1)
                    edge = Edge.Bottom;
                else if (offset.Columns == 1)
                    edge = Edge.Left;
                else if (offset.Columns == -1)
                    edge = Edge.Right;

                rNew.Contract(edge, 1);
                r = oldActiveRegion.Break(rNew).FirstOrDefault();
            }

            if (r != null && IsDataSheetActive)
                await ScrollToContainRegion(r);
        }
        else
        {
            CollapseAndMoveSelection(offset);
            if (IsDataSheetActive)
                await ScrollToActiveCellPosition();
        }

        return true;
    }

    private async Task<bool> HandleWindowPaste(ClipboardEventArgs arg)
    {
        if (!IsDataSheetActive)
            return false;

        if (!_sheet.Selection.Regions.Any())
            return false;

        if (_sheet.Editor.IsEditing)
            return false;

        var posnToInput = _sheet.Selection.GetInputPosition();

        var range = _sheet.InsertDelimitedText(arg.Text, posnToInput);
        if (range == null)
            return false;

        _sheet.Selection.Set(range);
        return true;
    }

    private async void HandleCellDoubleClick(object? sender, SheetPointerEventArgs args)
    {
        if (args.Row < 0 || args.Col < 0 || args.Row >= _sheet.NumRows || args.Col >= _sheet.NumCols)
            return;

        await BeginEdit(args.Row, args.Col, EditEntryMode.Mouse);
    }

    private async Task BeginEdit(int row, int col, EditEntryMode mode, string entryChar = "")
    {
        if (this.IsReadOnly)
            return;

        _sheet.Selection.CancelSelecting();
        _sheet.Editor.BeginEdit(row, col, false, mode, entryChar);
    }

    private void HandleCellMouseOver(object? sender, SheetPointerEventArgs args)
    {
        _sheet.Selection.UpdateSelectingEndPosition(args.Row, args.Col);
    }

    private async Task<bool> AcceptEditAndMoveActiveSelection(Axis axis, int amount)
    {
        var acceptEdit = !_sheet.Editor.IsEditing || _sheet.Editor.AcceptEdit();
        _sheet.Selection.MoveActivePosition(axis, amount);
        if (IsDataSheetActive)
            await ScrollToActiveCellPosition();
        return acceptEdit;
    }

    private async Task ScrollToActiveCellPosition()
    {
        var cellRect =
            _sheet.Cells.GetMerge(_sheet.Selection.ActiveCellPosition.row, _sheet.Selection.ActiveCellPosition.col) ??
            new Region(_sheet.Selection.ActiveCellPosition.row, _sheet.Selection.ActiveCellPosition.col);
        await ScrollToContainRegion(cellRect);
    }

    /// <summary>
    /// Increases the size of the active selection, around the active cell position
    /// </summary>
    private void GrowActiveSelection(Offset offset)
    {
        if (_sheet.Selection.ActiveRegion == null)
            return;

        var selPosition = _sheet.Selection.ActiveCellPosition;
        if (offset.Columns != 0)
        {
            if (offset.Columns == -1)
            {
                if (selPosition.col < _sheet.Selection.ActiveRegion.GetEdge(Edge.Right).Right)
                    _sheet.Selection.ContractEdge(Edge.Right, 1);
                else
                    _sheet.Selection.ExpandEdge(Edge.Left, 1);
            }
            else if (offset.Columns == 1)
                if (selPosition.col > _sheet.Selection.ActiveRegion.GetEdge(Edge.Left).Left)
                    _sheet.Selection.ContractEdge(Edge.Left, 1);
                else
                    _sheet.Selection.ExpandEdge(Edge.Right, 1);
        }

        if (offset.Rows != 0)
        {
            if (offset.Rows == -1)
            {
                if (selPosition.row < _sheet.Selection.ActiveRegion.GetEdge(Edge.Bottom).Bottom)
                    _sheet.Selection.ContractEdge(Edge.Bottom, 1);
                else
                    _sheet.Selection.ExpandEdge(Edge.Top, 1);
            }
            else if (offset.Rows == 1)
            {
                if (selPosition.row > _sheet.Selection.ActiveRegion.GetEdge(Edge.Top).Top)
                    _sheet.Selection.ContractEdge(Edge.Top, 1);
                else
                    _sheet.Selection.ExpandEdge(Edge.Bottom, 1);
            }
        }
    }

    private void CollapseAndMoveSelection(Offset offset)
    {
        if (_sheet.Selection.ActiveRegion == null)
            return;

        if (_sheet.Selection.IsSelecting)
            return;

        var posn = _sheet.Selection.ActiveCellPosition;

        _sheet.Selection.Set(posn.row, posn.col);
        _sheet.Selection.MoveActivePositionByRow(offset.Rows);
        _sheet.Selection.MoveActivePositionByCol(offset.Columns);
    }
    
    /// <summary>
    /// Set the datasheet as active, which controls whether the sheet is ready to receive keyboard input events.
    /// </summary>
    /// <param name="active"></param>
    public async Task SetActiveAsync(bool active = true)
    {
        if (active == IsDataSheetActive)
            return;

        if (active)
            await WindowEventService.PreventDefault("keydown");
        else
            await WindowEventService.CancelPreventDefault("keydown");

        IsDataSheetActive = active;
        await OnSheetActiveChanged.InvokeAsync(new SheetActiveEventArgs(this, active));
    }

    private async Task ScrollToContainRegion(IRegion region)
    {
        /*var left = _cellLayoutProvider.ComputeLeftPosition(region);
        var top = _cellLayoutProvider.ComputeTopPosition(region);
        var right = _cellLayoutProvider.ComputeRightPosition(region);
        var bottom = _cellLayoutProvider.ComputeBottomPosition(region);

        var scrollInfo = await _virtualizer.InvokeAsync<ViewportScrollInfo>("getViewportInfo", _wholeSheetDiv);
        if (ShowRowHeadings && StickyHeadings)
        {
            scrollInfo.VisibleLeft += _cellLayoutProvider.RowHeadingWidth;
            scrollInfo.ContainerWidth -= _cellLayoutProvider.RowHeadingWidth;
        }

        if (ShowColHeadings && StickyHeadings)
        {
            scrollInfo.VisibleTop += _cellLayoutProvider.ColHeadingHeight;
            scrollInfo.ContainerHeight -= _cellLayoutProvider.ColHeadingHeight;
        }

        double scrollToY = scrollInfo.ParentScrollTop;
        double scrollToX = scrollInfo.ParentScrollLeft;

        bool doScroll = false;

        if (top < scrollInfo.VisibleTop || bottom > scrollInfo.VisibleTop + scrollInfo.ContainerHeight)
        {
            var bottomDist = bottom - (scrollInfo.VisibleTop + scrollInfo.ContainerHeight);
            var topDist = top - scrollInfo.VisibleTop;

            var scrollYDist = Math.Abs(bottomDist) < Math.Abs(topDist)
                ? bottomDist
                : topDist;

            scrollToY = Math.Round(scrollInfo.ParentScrollTop + scrollYDist, 1);
            doScroll = true;
        }

        if (left < scrollInfo.VisibleLeft || right > scrollInfo.VisibleLeft + scrollInfo.ContainerWidth)
        {
            var rightDist = right - (scrollInfo.VisibleLeft + scrollInfo.ContainerWidth);
            var leftDist = left - scrollInfo.VisibleLeft;

            var scrollXDist = Math.Abs(rightDist) < Math.Abs(leftDist)
                ? rightDist
                : leftDist;

            scrollToX = Math.Round(scrollInfo.ParentScrollLeft + scrollXDist, 1);
            doScroll = true;
        }

        if (doScroll)
            await _virtualizer.InvokeVoidAsync("scrollTo", _wholeSheetDiv, scrollToX, scrollToY, "instant");*/
    }

    /// <summary>
    /// Copies current selection to clipboard
    /// </summary>
    public async Task<bool> CopySelectionToClipboard()
    {
        if (_sheet.Selection.IsSelecting)
            return false;

        // Can only handle single selections for now
        var region = _sheet.Selection.ActiveRegion;
        if (region == null)
            return false;

        await ClipboardService.Copy(region, _sheet);
        return true;
    }

    private Type GetCellRendererType(string type)
    {
        if (CustomCellTypeDefinitions.TryGetValue(type, out var definition))
            return definition.RendererType;

        return typeof(TextRenderer);
    }

    private Dictionary<string, object> GetCellRendererParameters(VisualCell visualCell)
    {
        return new Dictionary<string, object>()
        {
            { "Cell", visualCell },
            { "Sheet", _sheet }
        };
    }

    private RenderFragment GetIconRenderFragment(string? cellIcon)
    {
        if (cellIcon != null && Icons.TryGetValue(cellIcon, out var rf))
            return rf;
        return _ => { };
    }

    protected override void OnAfterRender(bool firstRender)
    {
        Console.WriteLine($"Datasheet rendered");
        base.OnAfterRender(firstRender);
    }
}