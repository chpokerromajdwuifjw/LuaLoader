using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NLua;

namespace GameMemoryEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            var engine = new Engine();
            engine.Start();
        }
    }

    // ================= ENGINE =================
    public class Engine
    {
        private Memory memory;
        private Lua lua;
        private HotkeyManager hotkeyManager;

        public Engine()
        {
            memory = new Memory();
            hotkeyManager = new HotkeyManager();
        }

        public void Start()
        {
            Console.Title = "Game Memory Engine";
            Log("[ENGINE] Starting...");

            InitializeLua();
            LoadMods();

            Log("[ENGINE] Ready - Press ESC to exit");

            // Wait for ESC key
            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                        break;
                }
                Thread.Sleep(100);
            }

            Log("[ENGINE] Shutting down...");
            hotkeyManager.Dispose();
            memory.Dispose();
            lua?.Dispose();
        }

        private void InitializeLua()
        {
            try
            {
                lua = new Lua();

                // Register memory functions
                lua.RegisterFunction("attach", memory, memory.GetType().GetMethod("Attach"));
                lua.RegisterFunction("read_int", memory, memory.GetType().GetMethod("ReadInt"));
                lua.RegisterFunction("write_int", memory, memory.GetType().GetMethod("WriteInt"));
                lua.RegisterFunction("read_float", memory, memory.GetType().GetMethod("ReadFloat"));
                lua.RegisterFunction("write_float", memory, memory.GetType().GetMethod("WriteFloat"));
                lua.RegisterFunction("read_byte", memory, memory.GetType().GetMethod("ReadByte"));
                lua.RegisterFunction("write_byte", memory, memory.GetType().GetMethod("WriteByte"));
                lua.RegisterFunction("resolve_ptr", memory, memory.GetType().GetMethod("ResolvePointer"));

                // Register utility functions
                lua["log"] = (Action<string>)LuaLog;
                lua["wait"] = (Action<int>)Wait;
                lua.RegisterFunction("onKeyDown", hotkeyManager, hotkeyManager.GetType().GetMethod("RegisterHotkey"));

                Log("[ENGINE] Lua runtime initialized");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to initialize Lua: {ex.Message}");
            }
        }

        private void LoadMods()
        {
            string modsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods");

            if (!Directory.Exists(modsPath))
            {
                Directory.CreateDirectory(modsPath);
                Log("[ENGINE] Created 'mods' directory");
                return;
            }

            var luaFiles = Directory.GetFiles(modsPath, "*.lua");

            if (luaFiles.Length == 0)
            {
                Log("[ENGINE] No mods found in 'mods' directory");
                return;
            }

            Log($"[ENGINE] Found {luaFiles.Length} mod(s)");

            foreach (var file in luaFiles)
            {
                string fileName = Path.GetFileName(file);
                try
                {
                    string code = File.ReadAllText(file);
                    Log($"[MOD] Loading: {fileName}");

                    Task.Run(() =>
                    {
                        try
                        {
                            lua.DoString(code);
                            Log($"[MOD] Loaded: {fileName}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[ERROR] {fileName}: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] Failed to read {fileName}: {ex.Message}");
                }
            }
        }

        private void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void LuaLog(string message)
        {
            Log($"[LUA] {message}");
        }

        private void Wait(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }
    }

    // ================= MEMORY CLASS =================
    public class Memory : IDisposable
    {
        private Process process;
        private IntPtr processHandle;

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, ref int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_ALL_ACCESS = 0x1F0FFF;

        public bool Attach(string processName)
        {
            try
            {
                // Remove .exe if present
                processName = processName.Replace(".exe", "");

                var processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    Console.WriteLine($"[ERROR] Process '{processName}' not found");
                    return false;
                }

                process = processes[0];
                processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);

                if (processHandle != IntPtr.Zero)
                {
                    Console.WriteLine($"[ENGINE] Attached to {processName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to attach: {ex.Message}");
                return false;
            }
        }

        public int ReadInt(long address)
        {
            if (processHandle == IntPtr.Zero) return 0;

            byte[] buffer = new byte[4];
            int bytesRead = 0;
            ReadProcessMemory(processHandle, (IntPtr)address, buffer, 4, ref bytesRead);
            return BitConverter.ToInt32(buffer, 0);
        }

        public void WriteInt(long address, int value)
        {
            if (processHandle == IntPtr.Zero) return;

            byte[] buffer = BitConverter.GetBytes(value);
            int bytesWritten = 0;
            WriteProcessMemory(processHandle, (IntPtr)address, buffer, 4, ref bytesWritten);
        }

        public float ReadFloat(long address)
        {
            if (processHandle == IntPtr.Zero) return 0f;

            byte[] buffer = new byte[4];
            int bytesRead = 0;
            ReadProcessMemory(processHandle, (IntPtr)address, buffer, 4, ref bytesRead);
            return BitConverter.ToSingle(buffer, 0);
        }

        public void WriteFloat(long address, float value)
        {
            if (processHandle == IntPtr.Zero) return;

            byte[] buffer = BitConverter.GetBytes(value);
            int bytesWritten = 0;
            WriteProcessMemory(processHandle, (IntPtr)address, buffer, 4, ref bytesWritten);
        }

        public byte ReadByte(long address)
        {
            if (processHandle == IntPtr.Zero) return 0;

            byte[] buffer = new byte[1];
            int bytesRead = 0;
            ReadProcessMemory(processHandle, (IntPtr)address, buffer, 1, ref bytesRead);
            return buffer[0];
        }

        public void WriteByte(long address, byte value)
        {
            if (processHandle == IntPtr.Zero) return;

            byte[] buffer = new byte[] { value };
            int bytesWritten = 0;
            WriteProcessMemory(processHandle, (IntPtr)address, buffer, 1, ref bytesWritten);
        }

        public long ResolvePointer(long baseAddress, params int[] offsets)
        {
            if (processHandle == IntPtr.Zero) return 0;

            try
            {
                long address = ReadInt(baseAddress);

                for (int i = 0; i < offsets.Length - 1; i++)
                {
                    address = ReadInt(address + offsets[i]);
                }

                return address + offsets[offsets.Length - 1];
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
                processHandle = IntPtr.Zero;
            }
        }
    }

    // ================= HOTKEY MANAGER =================
    public class HotkeyManager : IDisposable
    {
        private Dictionary<ConsoleKey, List<Action>> keyMap = new Dictionary<ConsoleKey, List<Action>>();
        private Dictionary<ConsoleKey, bool> keyStates = new Dictionary<ConsoleKey, bool>();
        private Thread hotkeyThread;
        private bool running = true;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public HotkeyManager()
        {
            hotkeyThread = new Thread(CheckKeys);
            hotkeyThread.IsBackground = true;
            hotkeyThread.Start();
        }

        public void RegisterHotkey(string keyName, Action callback)
        {
            ConsoleKey key;
            if (!Enum.TryParse(keyName, true, out key))
            {
                // Try to parse as VK code
                if (keyName.StartsWith("VK_") || keyName.Length == 2)
                {
                    Console.WriteLine($"[HOTKEY] Registered custom key: {keyName}");
                }
                return;
            }

            if (!keyMap.ContainsKey(key))
            {
                keyMap[key] = new List<Action>();
                keyStates[key] = false;
            }

            keyMap[key].Add(callback);
            Console.WriteLine($"[HOTKEY] Registered: {key}");
        }

        private void CheckKeys()
        {
            while (running)
            {
                foreach (var kvp in keyMap)
                {
                    // Convert ConsoleKey to virtual key code
                    int vkCode = (int)kvp.Key;

                    // For function keys
                    if (kvp.Key >= ConsoleKey.F1 && kvp.Key <= ConsoleKey.F24)
                    {
                        vkCode = 0x70 + (kvp.Key - ConsoleKey.F1);
                    }

                    bool isPressed = (GetAsyncKeyState(vkCode) & 0x8000) != 0;

                    if (isPressed && !keyStates[kvp.Key])
                    {
                        keyStates[kvp.Key] = true;

                        foreach (var callback in kvp.Value)
                        {
                            try
                            {
                                Task.Run(() => callback?.Invoke());
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR] Hotkey callback failed: {ex.Message}");
                            }
                        }
                    }
                    else if (!isPressed)
                    {
                        keyStates[kvp.Key] = false;
                    }
                }

                Thread.Sleep(10);
            }
        }

        public void Dispose()
        {
            running = false;
            hotkeyThread?.Join(1000);
        }
    }
}