using System;
using System.Reflection;

namespace DD2Estate.Estate
{
    /// <summary>
    /// Marks a static class that plugs itself into the estate at start-up (building screens, save sections,
    /// week handlers). The class must have a <c>static void Register()</c> method. Discovered by reflection so
    /// that adding a system never means editing a shared registration list.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class EstateModuleAttribute : Attribute
    {
    }

    internal static class EstateModules
    {
        public static void RegisterAll()
        {
            foreach (var type in typeof(EstateModules).Assembly.GetTypes())
            {
                if (type.GetCustomAttribute<EstateModuleAttribute>() == null) continue;
                var register = type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (register == null)
                {
                    Plugin.Log.LogError("estate module " + type.Name + " has no static Register()");
                    continue;
                }
                try { register.Invoke(null, null); }
                catch (Exception e) { Plugin.Log.LogError("estate module " + type.Name + " failed to register: " + (e.InnerException ?? e)); }
            }
        }
    }
}
