﻿using BlazorDatasheet.Data;
using BlazorDatasheet.Interfaces;

namespace BlazorDatasheet.Commands;

public class ClearCellsCommand : IUndoableCommand
{
    private readonly IEnumerable<IRegion> _ranges;
    private readonly List<ValueChange> _clearCommandOccurences;

    public ClearCellsCommand(IEnumerable<IRegion> ranges)
    {
        _ranges = ranges.Select(x => x.Copy()).ToList();
        _clearCommandOccurences = new List<ValueChange>();
    }

    public ClearCellsCommand(IRegion region) : this(new List<IRegion>() { region })
    {
    }

    public bool Execute(Sheet sheet)
    {
        foreach (var range in _ranges)
        {
            var rangeInSheet = range
                .GetIntersection(sheet.Region);
            foreach (var cellPosition in rangeInSheet)
            {
                if (cellPosition.IsInvalid)
                    continue;
                var cell = sheet.GetCell(cellPosition);
                var oldValue = cell.GetValue();

                // When this is redone it'll update the new value to the old value.
                _clearCommandOccurences.Add(
                    new ValueChange(cellPosition.Row, cellPosition.Col, oldValue));
            }
        }

        sheet.ClearCelllsImpl(_ranges);
        return true;
    }

    public bool Undo(Sheet sheet)
    {
        sheet.SetCellValuesImpl(_clearCommandOccurences);
        return true;
    }
}