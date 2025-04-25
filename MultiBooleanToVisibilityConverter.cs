using System;
using System.Globalization;
using System.Linq; // 需要 System.Linq
using System.Windows; // 需要引用 PresentationCore
using System.Windows.Data; // 需要引用 System.Windows

namespace DesktopToDo // 确保命名空间正确
{
    public class MultiBooleanToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 检查所有值是否都为布尔型的 true
            bool allTrue = values != null && values.All(v => v is bool b && b);
            return allTrue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("Cannot convert back from Visibility to multiple booleans.");
        }
    }
}