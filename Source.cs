using System;
using System.Threading;
using Microsoft.SPOT;
using GHIElectronics.NETMF.USBHost;

namespace Ascended.Controllers
{
    /// <summary>
    /// State of the LED around the XBox 360's Guide button.
    /// </summary>
    public enum Xbox360LedState
    {
        Off = 0,
        AllBlinking = 1,
        TopLeftBlinkThenOn=2,
        TopRightBlinkThenOn=3,
        BottomLeftBlinkThenOn=4,
        BottomRightBlinkThenOn=5,
        TopLeftOn=6,
        TopRightOn=7,
        BottomLeft=8,
        BottomRight=9,
        Rotate=10,
        Blink=11,
        BlinkSlower=12,
        RotateWithTwoLights=13,
        BlinkAlt=14,
        BlinkOnce=15
    }

    public class XBox360Controller
    {
        const int vendorId = 1118;
        const int productId = 654;

        USBH_RawDevice XBoxJoystick;

        USBH_RawDevice.Pipe XBoxInputPipe;
        USBH_RawDevice.Pipe XBoxOutputPipe;

        Thread XBoxThread;
        byte[] XBoxJoystickData = null;

        public bool IsConnected;

        // Declare the delegate (if using non-generic pattern).
        public delegate void XBoxConnectedHandler(object sender);

        // Declare the event.
        public event XBoxConnectedHandler ControllerConnected;
                    
        /// <summary>
        /// Buttons on the XBox 360 Controller
        /// </summary>
        public enum XBoxButton
        {
            // byte location | bit location
            /// <summary>
            /// Back button to the left of the Guide button.
            /// </summary>
            Back = (2<<8) | 0x20,
            /// <summary>
            /// Start button to the right of the Guide button.
            /// </summary>
            Start = (2<<8) | 0x10,
            /// <summary>
            /// Centre Guide button.
            /// </summary>
            Guide = (3<<8) | 0x04,
            /// <summary>
            /// Left shoulder button which has LB imprinted in it.
            /// </summary>
            LB = (3<<8) | 0x01,
            /// <summary>
            /// Right sholder button which has RB imprinted in it.
            /// </summary>
            RB = (3<<8) | 0x02,
            /// <summary>
            /// Up button on the hat switch.
            /// </summary>
            Up = (2<<8) | 0x01,
            /// <summary>
            /// Down button on the hat switch.
            /// </summary>
            Down = (2<<8) | 0x02,
            /// <summary>
            /// Left button on the hat switch.
            /// </summary>
            Left = (2<<8) | 0x04,
            /// <summary>
            /// Right button on the hat switch.
            /// </summary>
            Right = (2<<8) | 0x08,
            /// <summary>
            /// A button.
            /// </summary>
            A = (3<<8) | 0x10,
            /// <summary>
            /// B button.
            /// </summary>
            B = (3<<8) | 0x20,
            /// <summary>
            /// X button.
            /// </summary>
            X = (3<<8) | 0x40,
            /// <summary>
            /// Y button.
            /// </summary>
            Y = (3<<8) | 0x80,
            /// <summary>
            /// Right trigger button.
            /// </summary>
            LH = (2 << 8) | 0x40,
            /// <summary>
            /// Left trigger button.
            /// </summary>
            RH = (2 << 8) | 0x80,
        }

        /// <summary>
        /// Analog trigger buttons.
        /// </summary>
        public enum AnalogTrigger
        {
            /// <summary>
            /// Left Trigger
            /// </summary>
            LT = 4,
            /// <summary>
            /// Right Trigger
            /// </summary>
            RT = 5,
        }

        /// <summary>
        /// Analog input controls
        /// </summary>
        public enum AnalogHat
        {
            /// <summary>
            /// The left (upper) hat input X axis
            /// </summary>
            LeftHatX = 6,
            /// <summary>
            /// The left (upper) hat input Y axis
            /// </summary>
            LeftHatY = 8,
            /// <summary>
            /// The right (lower) hat input X axis
            /// </summary>
            RightHatX = 10,
            /// <summary>
            /// The right (lower) hat input Y axis
            /// </summary>
            RightHatY = 12,
        }

        public XBox360Controller()
        {
            // Subscribe to USBH event.
            USBHostController.DeviceConnectedEvent += DeviceConnectedEvent;
            // Unsubscribe to USBH event.
            USBHostController.DeviceDisconnectedEvent += DeviceDisconnectedEvent;
        }

        /// <summary>
        /// Get the state of a button on the controller
        /// </summary>
        /// <param name="button">Button to check state of</param>
        /// <returns>Button state</returns>
        public bool GetButton(XBoxButton button)
        {
            if (XBoxJoystickData == null)
                return false;
            lock (XBoxJoystickData)
            {
                if ((XBoxJoystickData[(uint)button >> 8] & ((byte)button & 0xff)) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get the position of the analog triggers
        /// </summary>
        /// <param name="trigger">Trigger to retrieve the position of</param>
        /// <returns>Position of the trigger button</returns>
        public byte GetAnalogT(AnalogTrigger trigger)
        {
            if (!IsConnected || XBoxJoystickData == null)
                return 0;

            lock (XBoxJoystickData)
            {
                return (byte)(XBoxJoystickData[(uint)trigger]);
            }
        }

        /// <summary>
        /// Get the position of the analog hat inputs
        /// </summary>
        /// <param name="hat">Hat input to get the state of</param>
        /// <returns>Position of the hat switch.</returns>
        public short GetAnalogHat(AnalogHat hat)
        {
            if (!IsConnected || XBoxJoystickData == null)
                return 0;

            lock (XBoxJoystickData)
            {
                return (short) ((XBoxJoystickData[(uint)hat+1] << 8) | XBoxJoystickData[(uint)hat]);
            }
        }

        private void DeviceDisconnectedEvent(USBH_Device device)
        {
            // this is not implemented yet
            if(IsConnected && device.TYPE == USBH_DeviceType.Unknown && device.VENDOR_ID == vendorId && productId == productId)
            {
                IsConnected = false;
                
                XBoxThread.Join();

                XBoxInputPipe = null;
                XBoxJoystickData = null;
            }
        }

        private void DeviceConnectedEvent(USBH_Device device)
        {
            if (XBoxJoystickData != null)
            {
                //we already have one connected so we will ignore any new events
                return;
            }

            if (device.TYPE == USBH_DeviceType.Unknown && device.VENDOR_ID == vendorId && device.PRODUCT_ID == productId)
            {
#if DEBUG
                Debug.Print("XBox Controller Found");
#endif

                XBoxJoystick = new USBH_RawDevice(device);

                // Get descriptors
                USBH_Descriptors.Configuration cd = XBoxJoystick.GetConfigurationDescriptors(0);

                // communication endpoint
                USBH_Descriptors.Endpoint XBoxInputEndPoint = null;
                USBH_Descriptors.Endpoint XboxOutputEndPoint = null;

                // look for HID class
                for (int i = 0; i < cd.interfaces.Length; i++)
                {
                    // found
                    if (cd.interfaces[i].bInterfaceSubclass == 0x5D && cd.interfaces[i].bInterfaceProtocol == 0x01)
                    {
                        if (cd.interfaces[i].endpoints.Length == 2)
                        {
                            int ep = 0;
                        
                            // set configuration
                            XBoxJoystick.SendSetupTransfer(0x00, 0x09, cd.bConfigurationValue, 0x00);

                            XBoxInputEndPoint = cd.interfaces[i].endpoints[ep];       // get endpoint
                            XBoxInputPipe = XBoxJoystick.OpenPipe(XBoxInputEndPoint);    // open pipe
                            XBoxInputPipe.TransferTimeout = 0;                  // recommended for interrupt transfers

                            ep++;

                            XboxOutputEndPoint = cd.interfaces[i].endpoints[ep];
                            XBoxOutputPipe = XBoxJoystick.OpenPipe(XboxOutputEndPoint);
                            XBoxOutputPipe.TransferTimeout = 0;


                            XBoxThread = new Thread(ReaderThread) { Priority = ThreadPriority.Highest /* we should read as fast as possible*/ };         // create the polling thread
                            XBoxThread.Start();


                            IsConnected = true;

                            if (ControllerConnected != null)
                            {
                                ControllerConnected(this);
                            }
                        }

                        //break;
                    }
                }
            }
        }

        /// <summary>
        /// Set the rumble power. Note: You must have ~400mA of power for using rumble!
        /// </summary>
        /// <param name="left">Set the left (low frequency) power</param>
        /// <param name="right">Set the right (high frequency) power</param>
        public void SetRumblePower(byte left, byte right)
        {
            byte[] rumbleData = new byte[]{ 0x00, 0x08, 0x00, left, right, 0x00, 0x00, 0x00 };

            try
            {
                XBoxOutputPipe.TransferData(rumbleData, 0, rumbleData.Length);
            }
            catch
            {
                USBH_ERROR e = USBHostController.GetLastError();
#if DEBUG
                Debug.Print("RUMBLE ERROR " + e);
#endif
            }
        }

        /// <summary>
        /// Set the state of the LED on the Guide button. This is only supported on XBox 360 controllers, not classic XBox controllers.
        /// </summary>
        /// <param name="status">State for the button</param>
        public void SetLedState(Xbox360LedState status)
        {
            byte[] ledData = new byte[] { 0x01, 0x03, (byte)status };

            try
            {
                XBoxOutputPipe.TransferData(ledData, 0, ledData.Length);
            }
            catch
            {
                USBH_ERROR e = USBHostController.GetLastError();
#if DEBUG
                Debug.Print("LED ERROR " + e);
#endif
            }
        }

        private void ReaderThread()
        {
            int count;

            // Maximum data is wMaxPacketSize
            XBoxJoystickData = new byte[XBoxInputPipe.PipeEndpoint.wMaxPacketSize];


            // Read every bInterval
            while (true)
            {
                Thread.Sleep(XBoxInputPipe.PipeEndpoint.bInterval);
                count = 0;
                lock (XBoxJoystickData)
                {
                    try
                    {
                        count = XBoxInputPipe.TransferData(XBoxJoystickData, 0, XBoxJoystickData.Length);

                    }
                    catch
                    {
                        USBH_ERROR e = USBHostController.GetLastError();
#if DEBUG
                        Debug.Print("ERROR" + e);
#endif
                        Array.Clear(XBoxJoystickData, 0, XBoxJoystickData.Length);
                        count = 0;
                    }
                }
                //if (count > 3)//this is useful to print the data to help in reverse engineering
                if(false)
                {
#if DEBUG
                    // Debug.Print("(dx, dy) = (" + (sbyte)(XBOX_joustickData[1]) + ", " + (sbyte)(XBOX_joustickData[2]) + ")");
                    int i = 0;
                    Debug.Print("=" +
                        ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " +
                        ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " +
                        ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " +
                        ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++]) + " " + ByteToHex(XBoxJoystickData[i++])
                        );
                    /////////// RT LT analog /////////////////////
                    if (XBoxJoystickData[4] > 200)
                        Debug.Print("LT");
                    if (XBoxJoystickData[5] > 200)
                        Debug.Print("RT");
                    /////// left hat analog //////////////////
                    //if (XBOX_joustickData[6] > 200)
                    //  Debug.Print("LH x");
                    //if (XBOX_joustickData[8] > 200)
                    //  Debug.Print("LH y");
#endif
                }

            }

        }

#if DEBUG
        public static string ByteToHex(byte b)
        {
            const string hex = "0123456789ABCDEF";
            int low = b & 0x0f;
            int high = b >> 4;
            string s = new string(new char[] { hex[high], hex[low] });
            return s;
        }        
#endif
    }
}
