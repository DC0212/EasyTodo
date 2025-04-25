using System;
using System.Globalization;
using System.Windows.Data; // 需要引用 System.Windows

namespace DesktopToDo // 确保命名空间正确
{
    [ValueConversion(typeof(bool), typeof(bool))]
    public class NegateBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 安全地处理 null 或非布尔值
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false; // 或者根据需要返回 DependencyProperty.UnsetValue
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 安全地处理 null 或非布尔值
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false; // 或者根据需要返回 DependencyProperty.UnsetValue
        }
    }
}