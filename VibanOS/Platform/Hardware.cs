using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cosmos.Kernel.HAL.Devices.Display;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.HAL.DriverKit.Resources;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;
using Cosmos.Kernel.System.Storage;
using VibanOS.Core;
using VibanOS.Graphics;

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

        // So viele Sektoren liest format auf einmal, wenn es prüft, ob die Platte leer ist.
        private const int ProbeSectors = 8;

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
        /// Arbeitet nur auf einer Platte, deren erstes MiB leer ist. Dort stehen die Partitionstabelle und der
        /// Kopf jedes gängigen Dateisystems, eine Platte mit Inhalt wird also nicht überschrieben.
        /// Rückgabe: null bei Erfolg, sonst der Grund als Text.
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

                // Der erste Sektor allein genügt nicht: ext4, btrfs und Swap lassen ihn frei und beginnen kurz dahinter.
                byte[] chunk = new byte[ProbeSectors * SectorSize];
                for (ulong lba = 0; lba < PartitionStart; lba += ProbeSectors)
                {
                    disk.ReadBlock(lba, ProbeSectors, chunk);
                    for (int i = 0; i < chunk.Length; i++)
                        if (chunk[i] != 0) return "Die Festplatte ist nicht leer und wird deshalb nicht angefasst. Für einen Neuanfang ein leeres Abbild anlegen.";
                }

                if (!fatRegistered) fatRegistered = VfsManager.RegisterFileSystem(FatDriver, new FatFileSystemType());
                Mbr.Create(disk);
                // 0x0C steht in einer MBR-Tabelle für FAT mit LBA-Adressen. COSMOS wertet die Kennung nicht aus,
                // es erkennt das Dateisystem am Inhalt. Die GUID zählt nur bei GPT-Platten.
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

        /// <summary>Größe und freier Platz der eingebundenen Platte in Bytes. false: keine Platte oder der Treiber weiß es nicht.</summary>
        public static bool DiskSpace(out ulong total, out ulong free)
        {
            total = free = 0;
            try
            {
                VfsStatFs stats;
                if (!VfsManager.TryStatFs(MountPoint, out stats)) return false;
                total = stats.Blocks * stats.BlockSize;
                free = stats.AvailableBlocks * stats.BlockSize;
                return true;
            }
            catch (Exception) { return false; }
        }

        // Vor dem Ausschalten aushängen: Dabei leert COSMOS den Schreibpuffer der Platte, erst dann ist alles sicher gespeichert.
        private static void UnmountDisk()
        {
            try { VfsManager.TryUnmount(MountPoint); }
            catch (Exception) { }
        }

        // ---- Bildschirm und Tastatur (Einstellungen) ----

        /// <summary>Name der Grafikkarte, wie COSMOS sie meldet, z. B. "firmware framebuffer" oder "vmware-svga ...".</summary>
        public static string ScreenName() { return Canvas.GetFullScreen().Name; }

        public static Resolution ScreenSize()
        {
            Canvas canvas = Canvas.GetFullScreen();
            return new Resolution(canvas.Width, canvas.Height);
        }

        /// <summary>
        /// Die Auflösungen, auf die der Bildschirm umschalten kann. Das Startbild von Limine (firmware framebuffer)
        /// kann nicht umschalten und meldet nur seine eine Auflösung. Umschalten kann unter COSMOS 3 nur
        /// die VMware-SVGA-Grafik, in QEMU mit -vga vmware.
        /// </summary>
        public static List<Resolution> ScreenModes()
        {
            List<Resolution> list = new List<Resolution>();
            IReadOnlyList<Mode> modes = Canvas.GetFullScreen().AvailableModes;
            for (int i = 0; i < modes.Count; i++) list.Add(new Resolution(modes[i].Width, modes[i].Height));
            return list;
        }

        /// <summary>
        /// Schaltet den Bildschirm um. Danach hat der Canvas einen neuen Puffer, Kernel.cs bemerkt die neue Größe
        /// im nächsten Durchlauf. false: Die Grafikkarte lehnt die Auflösung ab oder kann gar nicht umschalten.
        /// </summary>
        public static bool SetScreenMode(int width, int height)
        {
            try
            {
                Canvas canvas = Canvas.GetFullScreen(new Mode(width, height, ColorDepth.ColorDepth32));
                return canvas.Width == width && canvas.Height == height;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false; // zu groß für den Grafikspeicher oder nicht in der Liste
            }
        }

        /// <summary>
        /// Bringt den Ausschnitt (x, y, width, height) von screen auf den Bildschirm. screen ist die Zeichenfläche über dem
        /// Puffer des Canvas. Canvas.Display() kopiert dagegen immer das ganze Bild und meldet der Grafikkarte den ganzen
        /// Bildschirm als geändert. Bei einer Mausbewegung wären das Megabytes für ein paar hundert geänderte Pixel.
        /// whole: kopiert wird auch nur der Ausschnitt, die Grafikkarte soll aber das ganze Bild neu anzeigen. Nötig, wenn
        /// QEMU das Bild streckt (Settings.Stretched).
        /// </summary>
        // COSMOS kennzeichnet die Schnittstelle der Grafiktreiber als Vorabversion (COSMOS0003). Bricht sie mit einer neuen
        // COSMOS-Version, hier wieder canvas.Display() nehmen: Das ist langsamer, zeigt aber dasselbe Bild.
#pragma warning disable COSMOS0003
        public static void Present(Surface screen, int x, int y, int width, int height, bool whole)
        {
            try
            {
                DisplayDevice device = DisplayManager.Primary;
                IDisplay display;
                if (device == null || !device.TryGetFacet(out display)) display = null;
                DeviceRegion framebuffer = display != null ? display.Framebuffer : null;
                DisplayMode mode = display != null ? display.Mode : default;
                // Was der Weg hier nicht kennt (andere Farbtiefe, kein Framebuffer), erledigt COSMOS wie bisher.
                if (framebuffer == null || mode.IsEmpty || mode.BitsPerPixel != 32 || framebuffer.Length < (ulong)mode.Height * (ulong)mode.Pitch)
                {
                    Canvas.GetFullScreen().Display();
                    return;
                }
                // Der Ausschnitt ist in Punkten, auf dem Bildschirm zählen Pixel (bei Retina doppelt so viele).
                x *= screen.Scale; y *= screen.Scale; width *= screen.Scale; height *= screen.Scale;
                int pitch = mode.Pitch / sizeof(int);
                int right = Math.Min(Math.Min(x + width, screen.PixelWidth), Math.Min(mode.Width, pitch));
                int bottom = Math.Min(Math.Min(y + height, screen.PixelHeight), mode.Height);
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                if (right <= x || bottom <= y) return;
                Span<int> target = framebuffer.As<int>();
                for (int row = y; row < bottom; row++)
                    screen.Pixels.AsSpan(row * screen.PixelWidth + x, right - x).CopyTo(target.Slice(row * pitch + x, right - x));
                // whole: QEMU streckt das Bild auf sein Fenster. Die Mac-Anzeige von QEMU zeichnet dann einen gemeldeten
                // Ausschnitt um Bruchteile eines Pixels versetzt neu, an Kanten sieht man das (etwa unter dem Mauszeiger).
                // Deshalb soll sie das ganze Bild neu zeichnen. Kopiert ist trotzdem nur der Ausschnitt.
                if (whole) display.Flush(0, 0, Math.Min(mode.Width, pitch), mode.Height);
                else display.Flush(x, y, right - x, bottom - y);
            }
            catch (InvalidOperationException)
            {
                // Die Anzeige verschwindet gerade (wie in Canvas.Display). Der nächste Durchlauf zeichnet neu.
            }
        }

        /// <summary>
        /// Zeigt nur die oberen height Pixelzeilen des Bildschirms, wenn die Grafik das kann (virtio-gpu). false: alles
        /// bleibt, wie es war. Siehe Settings.Rows, wozu das nötig ist.
        /// </summary>
        public static bool CropScreen(int height)
        {
            try
            {
                DisplayDevice device = DisplayManager.Primary;
                IDisplay display;
                if (device == null || !device.TryGetFacet(out display) || display.Name != "virtio-gpu") return false;
                int full = display.Mode.Height;
                if (height < 16 || height >= full) return false; // unter 16 Zeilen lehnt virtio-gpu ab
                GpuHeight(display) = height;
                if (GpuSetScanout(display)) return true;
                GpuHeight(display) = full;
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
#pragma warning restore COSMOS0003

        // ponytail: COSMOS 3 liest die Größe von virtio-gpu einmal beim Start und zeigt immer die ganze Fläche, ändern lässt
        // sie sich öffentlich nicht. Deshalb wie bei den I/O-Ports unten der Notausgang UnsafeAccessor: die Höhe im Treiber
        // kleiner setzen und SET_SCANOUT noch einmal schicken. Der Puffer bleibt, QEMU zeigt nur weniger Zeilen davon.
        // Bricht das mit einer neuen COSMOS-Version, wirft der Aufruf, CropScreen meldet false und das Bild bleibt wie bisher.
        // Sauberer Weg, sobald COSMOS ihn hat: die Größe über IDisplayModes setzen.
        private const string VirtioGpu = "Cosmos.Kernel.Drivers.Virtio.Display.VirtioGpu.VirtioGpuState, Cosmos.Kernel.Drivers";

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_height")]
        private static extern ref int GpuHeight([UnsafeAccessorType(VirtioGpu)] object state);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SetScanout")]
        private static extern bool GpuSetScanout([UnsafeAccessorType(VirtioGpu)] object state);

        public static void SetKeyboardLayout(bool german)
        {
            if (german) KeyboardManager.SetLayout(new DEStandardLayout());
            else KeyboardManager.SetLayout(new USStandardLayout());
        }

        // ---- I/O-Ports (VIB-9) ----

        public static byte In(int port) { return Read8(null, (ushort)port); }

        public static void Out(int port, byte value) { Write8(null, (ushort)port, value); }

        public static void Out16(int port, ushort value) { Write16(null, (ushort)port, value); }

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

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Write16")]
        private static extern void Write16([UnsafeAccessorType(PortIo)] object io, ushort port, ushort value);
    }
}
