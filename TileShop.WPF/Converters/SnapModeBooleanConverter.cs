﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;
using TileShop.WPF.Helpers;

namespace TileShop.WPF.Converters
{
    public class SnapModeBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is SnapMode mode)
            {
                if (mode == SnapMode.Element)
                    return false;
                else if (mode == SnapMode.Pixel)
                    return true;
            }

            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is bool snapBool)
            {
                if (snapBool == false)
                    return SnapMode.Element;
                else
                    return SnapMode.Pixel;
            }

            return Binding.DoNothing;
        }
    }
}