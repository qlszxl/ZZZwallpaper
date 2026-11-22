using LumaWall.Contracts;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LumaWall.ControlCenter;

// The slider's gesture step is deliberately separate from typed precision.
internal static class NumericValueRules
{
    internal static bool TryRead(string draft,double minimum,double maximum,int precision,out double value,out string error)
    {
        value=0;error="";
        if(draft.Length>64){error=UiLanguage.Text("数字过长");return false;}
        string text=draft.Trim().Normalize(NormalizationForm.FormKC);
        string separator=CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if(separator!=".")text=text.Replace(separator,".",StringComparison.Ordinal);
        if(!Regex.IsMatch(text,@"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)$",RegexOptions.CultureInvariant)||
            !double.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)||!double.IsFinite(value)) {
            error=UiLanguage.Text("请输入完整数字");return false;
        }
        if(precision==0&&value!=Math.Truncate(value)){error=UiLanguage.Text("仅支持整数");return false;}
        if(value<minimum||value>maximum){error=Range(minimum,maximum,precision);return false;}
        value=Math.Round(value,precision,MidpointRounding.AwayFromZero);
        if(value<minimum||value>maximum){error=Range(minimum,maximum,precision);return false;}
        return true;
    }
    internal static string Format(double value,int precision)=>value.ToString("F"+precision,CultureInfo.CurrentCulture);
    internal static string Range(double min,double max,int precision)=>UiLanguage.Format($"请输入 {min.ToString("0.####",CultureInfo.CurrentCulture)}～{max.ToString("0.####",CultureInfo.CurrentCulture)}")+(precision==0?UiLanguage.Text(" 的整数"):UiLanguage.Format($"，最多 {precision} 位小数"));
}
