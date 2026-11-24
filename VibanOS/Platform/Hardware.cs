using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace VibanOS.Platform
{
    /// <summary>
    /// Die einzige Stelle neben Kernel.cs, die COSMOS direkt anspricht. Alles andere geht über
    /// diese Klasse (bzw. über Syscalls), damit der Kern unabhängig von COSMOS bleibt.
    /// tools/Preview ersetzt diese Datei durch eine Attrappe.
    /// </summary>
    public static class Hardware
    {
        /// <summary>Hier hängt die Festplatte im Dateisystem von COSMOS, mit abschließendem Trenner für Vfs.Init.</summary>
        public const string DiskRoot = "/mnt/";

        private const string MountPoint = "/mnt";
        private const string FatDriver = "fat";

        // Bis hier (in Sektoren) bleibt die Platte vor der Partition frei, üblich ist 1 MiB.
        private const ulong PartitionStart = 2048;
        private const int SectorSize = 512;

        private static bool fatRegistered;

        public static void Shutdown()
        {
            UnmountDisk();
            Power.Shutdown();
        }

        public static void Reboot()
        {
            UnmountDisk();
            Power.Reboot();
        }

        // ---- Festplatte (VIB-2) ----

        /// <summary>Hängt die erste FAT-Partition unter /mnt ein. false: Es gibt (noch) keine formatierte Festplatte.</summary>
        public static bool MountDisk()
        {
            if (!fatRegistered) fatRegistered = VfsManager.RegisterFileSystem(FatDriver, new FatFileSystemType());
            // Die Liste einmal holen: COSMOS tauscht sie aus, wenn ein Datenträger dazukommt oder verschwindet.
            IReadOnlyList<Partition> partitions = StorageManager.Partitions;
            for (int i = 0; i < partitions.Count; i++)
            {
                try
                {
                    VfsMount mount;
                    if (VfsManager.TryMount(FatDriver, partitions[i], MountFlags.None, MountPoint, out mount)) return true;
                }
                catch (Exception)
                {
                    // Ein Lesefehler auf einem Datenträger soll den Start nicht verhindern. Weiter mit dem nächsten.
                }
            }
            return false;
        }

        /// <summary>
        /// Richtet eine leere Festplatte ein: Partitionstabelle, eine FAT-Partition über die ganze Platte, einhängen.
        /// Arbeitet nur auf einer Platte, deren erster Sektor leer ist. Eine Platte mit Partitionstabelle oder
        /// Dateisystem wird nie überschrieben. Rückgabe: null bei Erfolg, sonst der Grund als Text.
        /// </summary>
        public static string FormatDisk()
        {
            try
            {
                IBlockDevice disk = StorageManager.PrimaryDevice;
                // Ohne Festplatte ist das erste Gerät die Start-CD, erkennbar an ihren 2048-Byte-Sektoren.
                if (disk == null || disk.BlockSize != SectorSize)
                    return "Keine Festplatte gefunden. VibanOS mit einer Festplatte starten (siehe README).";
                if (disk.BlockCount <= 2 * PartitionStart)
                    return "Die Festplatte ist zu klein.";

                byte[] first = new byte[SectorSize];
                disk.ReadBlock(0, 1, first);
                for (int i = 0; i < first.Length; i++)
                    if (first[i] != 0) return "Die Festplatte ist nicht leer und wird deshalb nicht angefasst. Für einen Neuanfang ein leeres Abbild anlegen.";

                if (!fatRegistered) fatRegistered = VfsManager.RegisterFileSystem(FatDriver, new FatFileSystemType());
                Mbr.Create(disk);
                // 0x0C ist die Kennung für FAT32 in einer MBR-Tabelle. Die GUID zählt nur bei GPT-Platten.
                if (!PartitionManager.Create(disk, PartitionStart, disk.BlockCount - PartitionStart, 0x0C, Gpt.BasicDataPartitionType))
                    return "Die Partition ließ sich nicht anlegen.";
                StorageManager.RescanPartitions(disk);
                IReadOnlyList<Partition> partitions = StorageManager.GetPartitions(disk);
                if (partitions.Count == 0) return "Die neue Partition wurde nicht gefunden.";

                // Ohne Angabe wählt COSMOS FAT12, FAT16 oder FAT32 passend zur Größe.
                if (!VfsManager.TryFormat(FatDriver, partitions[0], new FatFormatOptions { VolumeLabel = "VIBANOS" }))
                    return "Das Formatieren ist fehlgeschlagen.";
                return MountDisk() ? null : "Formatiert, aber das Einhängen ist fehlgeschlagen. Bitte neu starten.";
            }
            catch (Exception e)
            {
                return "Fehler beim Einrichten der Festplatte: " + e.Message;
            }
        }

        // Vor dem Ausschalten aushängen: Dabei leert COSMOS den Schreibpuffer der Platte, erst dann ist alles sicher gespeichert.
        private static void UnmountDisk()
        {
            try { VfsManager.TryUnmount(MountPoint); }
            catch (Exception) { }
        }

        // ---- I/O-Ports (VIB-9) ----

        public static byte In(int port) { return Read8(null, (ushort)port); }

        public static void Out(int port, byte value) { Write8(null, (ushort)port, value); }

        // ponytail: COSMOS 3 gibt dem Kernel keine öffentlichen I/O-Ports. Die Aufgabe VIB-9 verlangt sie aber
        // (inb, outb, COM1-Treiber). Deshalb der von COSMOS beschriebene Notausgang UnsafeAccessor auf die
        // interne Klasse Native.IO. Er gilt nur auf x64 und kann mit einer neuen COSMOS-Version brechen, dann
        // wirft der erste Aufruf eine Ausnahme und der Treiber com1 meldet "gestoppt".
        // Sauberer Weg, sobald nötig: ein eigener Treiber über das Driver Kit von COSMOS.
        private const string PortIo = "Cosmos.Kernel.Core.Native+IO, Cosmos.Kernel.Core";

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Read8")]
        private static extern byte Read8([UnsafeAccessorType(PortIo)] object io, ushort port);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Write8")]
        private static extern void Write8([UnsafeAccessorType(PortIo)] object io, ushort port, byte value);
    }
}
