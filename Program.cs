using System;
using System.Diagnostics;
using System.Windows;
using Newtonsoft.Json.Linq;
using WebSocket4Net;
using WindowsInput;

namespace RemoteCloudPC
{
    class Program
    {
        static readonly int ScreenWidth = (int)SystemParameters.PrimaryScreenWidth;
        static readonly int ScreenHeight = (int)SystemParameters.PrimaryScreenHeight;
        static readonly InputSimulator inputSimulator = new InputSimulator();

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
                    break;
            }
        }
    }
}
