using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Newtonsoft.Json.Linq;
using WebSocket4Net;
using WindowsInput;
using WindowsInput.Native;

namespace RemoteCloudPC
{
    class Program
    {
        static readonly int ScreenWidth = (int)SystemParameters.PrimaryScreenWidth;
        static readonly int ScreenHeight = (int)SystemParameters.PrimaryScreenHeight;
        static readonly InputSimulator inputSimulator = new InputSimulator();

        static readonly IReadOnlyDictionary<string, VirtualKeyCode> keyCodeTable =
            Enum.GetValues(typeof(VirtualKeyCode))
                .Cast<VirtualKeyCode>()
                .Distinct()
                .ToDictionary(value => Enum.GetName(typeof(VirtualKeyCode), value));

        static readonly IReadOnlyDictionary<string, string> keyTranslationTable = new Dictionary<string, string> {
            { "BACKSPACE", "BACK" },
            { "ENTER", "RETURN" },
            { "ALT", "MENU" },
            { "ARROWLEFT", "LEFT" },
            { "ARROWRIGHT", "RIGHT" },
            { "ARROWUP", "UP" },
            { "ARROWDOWN", "DOWN" },
        };

        static string serverAddress;
        static string hostId;
        static Process screenCaptureProcess;

        static void Main(string[] args)
        {
            if (args.Length != 1)
                return;
            serverAddress = args[0];

            using (var webSocket = new WebSocket("ws://" + serverAddress + ":8080")) {
                webSocket.MessageReceived += WebSocket_MessageReceived;
                webSocket.Opened += (s, e) => {
                    webSocket.Send("{ \"type\": \"connect-host\", "
                        + "\"screenWidth\": " + ScreenWidth + ", "
                        + "\"screenHeight\": " + ScreenHeight + " }");
                };
                webSocket.Open();

                Console.WriteLine("Press any key to exit.");
                Console.ReadLine();

                webSocket.MessageReceived -= WebSocket_MessageReceived;
                webSocket.Send("{ \"type\": \"disconnect-host\", \"hostid\": " + hostId + " }");
            }
        }

        static void WebSocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var message = JObject.Parse(e.Message);

            switch (message["type"].ToString()) {
                case "create-hostid":
                    hostId = message["hostid"].ToString();
                    Console.WriteLine("Your host id is {0}.", hostId);
                    break;

                case "connect-guest":
                    screenCaptureProcess = Process.Start(
                        "ffmpeg.exe",
                        "-f gdigrab -draw_mouse 1 -show_region 1 -framerate 30"
                        + " -video_size " + ScreenWidth + "x" + ScreenHeight
                        + " -i desktop -f mpeg1video -b:v 2048k"
                        + " http://" + serverAddress + ":8082/" + hostId
                    );
                    break;

                case "disconnect-guest":
                    screenCaptureProcess.Kill();
                    break;

                case "mouse-move": {
                    var x = ushort.MaxValue * message["x"].Value<double>() / ScreenWidth;
                    var y = ushort.MaxValue * message["y"].Value<double>() / ScreenHeight;
                    inputSimulator.Mouse.MoveMouseTo(x, y);
                    break;
                }

                case "mouse-up": {
                    string button = message["button"].ToString();
                    if (button == "left")
                        inputSimulator.Mouse.LeftButtonUp();
                    else if (button == "right")
                        inputSimulator.Mouse.RightButtonUp();
                    break;
                }

                case "mouse-down": {
                    string button = message["button"].ToString();
                    if (button == "left")
                        inputSimulator.Mouse.LeftButtonDown();
                    else if (button == "right")
                        inputSimulator.Mouse.RightButtonDown();
                    break;
                }

                case "key-down":
                    OnKeyDown(message);
                    break;
            }
        }

        static void OnKeyDown(JObject message)
        {
            var key = message["key"].ToString();
            var shift = message["shift"].Value<bool>();
            var ctrl = message["ctrl"].Value<bool>();
            var alt = message["alt"].Value<bool>();

            if (key.Length == 1 && !ctrl && !alt) {
                inputSimulator.Keyboard.TextEntry(key);
                return;
            }

            key = key.ToUpper();
            if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
                key = "VK_" + key;

            string translatedKey;
            var exists = keyTranslationTable.TryGetValue(key, out translatedKey);
            if (!exists)
                translatedKey = key;

            var modifiers = new List<VirtualKeyCode>(3);
            if (shift)
                modifiers.Add(VirtualKeyCode.SHIFT);
            if (ctrl)
                modifiers.Add(VirtualKeyCode.CONTROL);
            if (alt)
                modifiers.Add(VirtualKeyCode.MENU);

            inputSimulator.Keyboard.ModifiedKeyStroke(modifiers, keyCodeTable[translatedKey]);
        }
    }
}
