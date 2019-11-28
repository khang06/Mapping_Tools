﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mapping_Tools.Components.Graph.Interpolation {
    public class InterpolatorHelper {
        private static readonly Type InterfaceType = typeof(IGraphInterpolator);

        private static Type[] _interpolators;
        public static Type[] GetInterpolators() {
            return _interpolators ?? (_interpolators = AppDomain.CurrentDomain.GetAssemblies()
                       .SelectMany(x => x.GetTypes())
                       .Where(x => InterfaceType.IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract &&
                                   x.GetCustomAttribute<IgnoreInterpolatorAttribute>() == null).ToArray());
        }

        public static string GetName(Type type) {
            var nameAttribute = type.GetCustomAttribute<DisplayNameAttribute>();
            return nameAttribute != null ? nameAttribute.DisplayName : type.Name;
        }

        public static IGraphInterpolator GetInterpolator(string name) {
            return (from interpolator in GetInterpolators() where GetName(interpolator) == name select GetInterpolator(interpolator)).FirstOrDefault();
        }

        public static IGraphInterpolator GetInterpolator(Type type) {
            return (IGraphInterpolator) Activator.CreateInstance(type);
        }
    }
}