using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Intervals;
using BlazorDatasheet.Formula.Core;

namespace BlazorDatasheet.Core.Data.Filter;

public class PatternFilter : IColumnFilter
{
    private readonly PatternFilterType _type;
    private readonly string _value;

    public PatternFilter(PatternFilterType type, string value)
    {
        _type = type;
        _value = value;
    }

    public bool Match(CellValue cellValue)
    {
        if (cellValue.Data?.ToString() == null)
            return false;
        switch (_type)
        {
            case PatternFilterType.StartsWith:
                return cellValue.Data.ToString()!.StartsWith(_value);
            case PatternFilterType.EndsWith:
                return cellValue.Data.ToString()!.EndsWith(_value);
            case PatternFilterType.Contains:
                return cellValue.Data.ToString()!.Contains(_value);
            case PatternFilterType.NotStartsWith:
                return !cellValue.Data.ToString()!.StartsWith(_value);
            case PatternFilterType.NotEndsWith:
                return !cellValue.Data.ToString()!.EndsWith(_value);
            case PatternFilterType.NotContains:
                return !cellValue.Data.ToString()!.Contains(_value);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

public enum PatternFilterType
{
    StartsWith,
    EndsWith,
    Contains,
    NotStartsWith,
    NotEndsWith,
    NotContains
}