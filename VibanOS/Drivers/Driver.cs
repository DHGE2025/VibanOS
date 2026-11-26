using System;
using System.Collections.Generic;

namespace VibanOS.Drivers
{
    public enum DriverStatus { Stopped, Running }

    /// <summary>Basisklasse aller Treiber (VIB-9).</summary>
    public abstract class Driver
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public DriverStatus Status = DriverStatus.Stopped;

        /// <summary>Richtet die Hardware ein. Wird bei jedem Start aufgerufen.</summary>
        protected abstract void Init();

        /// <summary>false: Die Hardware ließ sich nicht einrichten, der Treiber bleibt gestoppt.</summary>
        public bool Start()
        {
            try { Init(); }
            catch (Exception) { return false; }
            Status = DriverStatus.Running;
            return true;
        }

        public virtual void Stop()
        {
            Status = DriverStatus.Stopped;
        }
    }

    /// <summary>Zentrale Treiberverwaltung: registrieren, starten, stoppen, auflisten.</summary>
    public class DriverManager
    {
        public readonly List<Driver> All = new List<Driver>();

        public void Register(Driver driver) { All.Add(driver); }

        public Driver Find(string name)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name) return All[i];
            return null;
        }

        public bool Start(string name)
        {
            Driver d = Find(name);
            return d != null && d.Start();
        }

        public bool Stop(string name)
        {
            Driver d = Find(name);
            if (d == null) return false;
            d.Stop();
            return true;
        }
    }
}
