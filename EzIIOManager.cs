using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using FASTECH;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EzIIOLib
{
    public enum EzIIODeviceType
    {
        I16O16,  // 16 inputs, 16 outputs
        I8O8     // 8 inputs, 8 outputs
    }

    public class EzIIOManager : IDisposable
    {
        private const int TCP = 0;

        private static readonly uint[] OUTPUT_PIN_MASKS_I16O16 = new uint[16]
        {
            0x10000,    // Pin 0
            0x20000,    // Pin 1
            0x40000,    // Pin 2
            0x80000,    // Pin 3
            0x100000,   // Pin 4
            0x200000,   // Pin 5
            0x400000,   // Pin 6
            0x800000,   // Pin 7
            0x1000000,  // Pin 8
            0x2000000,  // Pin 9
            0x4000000,  // Pin 10
            0x8000000,  // Pin 11
            0x10000000, // Pin 12
            0x20000000, // Pin 13
            0x40000000, // Pin 14
            0x80000000  // Pin 15
        };

        private static readonly uint[] OUTPUT_PIN_MASKS_I8O8 = new uint[8]
        {
            0x100,     // Pin 0
            0x200,     // Pin 1
            0x400,     // Pin 2
            0x800,     // Pin 3
            0x1000,    // Pin 4
            0x2000,    // Pin 5
            0x4000,    // Pin 6
            0x8000     // Pin 7
        };

        private readonly int boardId;
        private readonly EzIIODeviceType deviceType;
        private bool isConnected;
        private bool isMonitoring;
        private Thread monitorThread;
        private CancellationTokenSource cancellationTokenSource;
        private readonly int inputPinCount;
        private readonly int outputPinCount;
        private readonly uint[] currentOutputMasks;

        // Add dictionaries for pin name mapping
        private readonly Dictionary<string, int> inputPinNameToNumber;
        private readonly Dictionary<string, int> outputPinNameToNumber;
        private readonly Dictionary<int, string> inputPinNumberToName;
        private readonly Dictionary<int, string> outputPinNumberToName;

        public ObservableCollection<PinStatus> InputPins { get; private set; }
        public ObservableCollection<PinStatus> OutputPins { get; private set; }

        public event EventHandler<bool> ConnectionStatusChanged;
        public event EventHandler<string> Error;
        public event EventHandler<(string Name, bool State)> InputStateChanged;
        public event EventHandler<(string Name, bool State)> OutputStateChanged;

        public bool IsConnected => isConnected;
        public EzIIODeviceType DeviceType => deviceType;

        public EzIIOManager(EzIIODeviceType deviceType = EzIIODeviceType.I16O16, int boardId = 0)
        {
            this.boardId = boardId;
            this.deviceType = deviceType;

            // Initialize pin name mappings
            inputPinNameToNumber = new Dictionary<string, int>();
            outputPinNameToNumber = new Dictionary<string, int>();
            inputPinNumberToName = new Dictionary<int, string>();
            outputPinNumberToName = new Dictionary<int, string>();

            // Set pin counts based on device type
            switch (deviceType)
            {
                case EzIIODeviceType.I8O8:
                    inputPinCount = 8;
                    outputPinCount = 8;
                    currentOutputMasks = OUTPUT_PIN_MASKS_I8O8;
                    break;
                case EzIIODeviceType.I16O16:
                default:
                    inputPinCount = 16;
                    outputPinCount = 16;
                    currentOutputMasks = OUTPUT_PIN_MASKS_I16O16;
                    break;
            }

            InitializePinCollections();
            cancellationTokenSource = new CancellationTokenSource();
        }

        // Add method to configure pin names
        public void ConfigurePinNames(Dictionary<string, int> inputPins, Dictionary<string, int> outputPins)
        {
            // Clear existing mappings
            inputPinNameToNumber.Clear();
            outputPinNameToNumber.Clear();
            inputPinNumberToName.Clear();
            outputPinNumberToName.Clear();

            // Configure input pins
            foreach (var pin in inputPins)
            {
                if (pin.Value >= 0 && pin.Value < inputPinCount)
                {
                    inputPinNameToNumber[pin.Key] = pin.Value;
                    inputPinNumberToName[pin.Value] = pin.Key;
                    InputPins[pin.Value].Name = pin.Key;
                }
            }

            // Configure output pins
            foreach (var pin in outputPins)
            {
                if (pin.Value >= 0 && pin.Value < outputPinCount)
                {
                    outputPinNameToNumber[pin.Key] = pin.Value;
                    outputPinNumberToName[pin.Value] = pin.Key;
                    OutputPins[pin.Value].Name = pin.Key;
                }
            }
        }

        private void InitializePinCollections()
        {
            InputPins = new ObservableCollection<PinStatus>();
            OutputPins = new ObservableCollection<PinStatus>();

            for (int i = 0; i < inputPinCount; i++)
            {
                InputPins.Add(new PinStatus { PinNumber = i, Name = $"Input{i}", State = false });
            }

            for (int i = 0; i < outputPinCount; i++)
            {
                OutputPins.Add(new PinStatus { PinNumber = i, Name = $"Output{i}", State = false });
            }
        }

        public bool Connect(string ipAddress)
        {
            try
            {
                if (!IPAddress.TryParse(ipAddress, out IPAddress ip))
                {
                    RaiseError("Invalid IP Address format");
                    return false;
                }

                if (EziMOTIONPlusELib.FAS_ConnectTCP(ip, boardId))
                {
                    isConnected = true;
                    StartMonitoring();
                    ConnectionStatusChanged?.Invoke(this, true);
                    return true;
                }
                else
                {
                    RaiseError("Connection failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseError($"Error connecting: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            if (isConnected)
            {
                StopMonitoring();
                EziMOTIONPlusELib.FAS_Close(boardId);
                isConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
            }
        }

        // New method to set output by pin name
        public bool SetOutputByName(string pinName, bool state)
        {
            if (!isConnected || !outputPinNameToNumber.ContainsKey(pinName))
            {
                RaiseError($"Invalid pin name or not connected: {pinName}");
                return false;
            }

            int pinNumber = outputPinNameToNumber[pinName];
            return SetOutputPin(pinNumber, state);
        }

        // New method to set output
        public bool SetOutput(string pinName)
        {
            return SetOutputByName(pinName, true);
        }

        // New method to clear output
        public bool ClearOutput(string pinName)
        {
            return SetOutputByName(pinName, false);
        }

        public bool SetOutputPin(int pinNumber, bool state)
        {
            if (!isConnected || pinNumber < 0 || pinNumber >= outputPinCount)
                return false;

            uint setMask = state ? currentOutputMasks[pinNumber] : 0;
            uint clearMask = state ? 0 : currentOutputMasks[pinNumber];

            return EziMOTIONPlusELib.FAS_SetOutput(boardId, setMask, clearMask) == EziMOTIONPlusELib.FMM_OK;
        }

        // New method to get input state by name
        public bool? GetInputState(string pinName)
        {
            if (!inputPinNameToNumber.ContainsKey(pinName))
                return null;

            int pinNumber = inputPinNameToNumber[pinName];
            return InputPins[pinNumber].State;
        }

        // New method to get output state by name
        public bool? GetOutputState(string pinName)
        {
            if (!outputPinNameToNumber.ContainsKey(pinName))
                return null;

            int pinNumber = outputPinNameToNumber[pinName];
            return OutputPins[pinNumber].State;
        }

        private void StartMonitoring()
        {
            isMonitoring = true;
            monitorThread = new Thread(MonitorPins)
            {
                IsBackground = true
            };
            monitorThread.Start();
        }

        private void StopMonitoring()
        {
            isMonitoring = false;
            monitorThread?.Join(1000);
        }

        private void MonitorPins()
        {
            while (isMonitoring && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Monitor Input Pins
                    uint currentInput = 0;
                    uint latch = 0;
                    if (EziMOTIONPlusELib.FAS_GetInput(boardId, ref currentInput, ref latch) == EziMOTIONPlusELib.FMM_OK)
                    {
                        UpdateInputPinStates(currentInput);
                    }

                    // Monitor Output Pins
                    uint currentOutput = 0;
                    uint status = 0;
                    if (EziMOTIONPlusELib.FAS_GetOutput(boardId, ref currentOutput, ref status) == EziMOTIONPlusELib.FMM_OK)
                    {
                        UpdateOutputPinStates(currentOutput);
                    }

                    Thread.Sleep(100); // Polling interval
                }
                catch (Exception ex)
                {
                    RaiseError($"Error monitoring pins: {ex.Message}");
                    Disconnect();
                    break;
                }
            }
        }

        private void UpdateInputPinStates(uint pinStates)
        {
            for (int i = 0; i < InputPins.Count; i++)
            {
                bool newState = (pinStates & (1u << i)) != 0;
                if (InputPins[i].State != newState)
                {
                    InputPins[i].State = newState;
                    if (inputPinNumberToName.ContainsKey(i))
                    {
                        InputStateChanged?.Invoke(this, (inputPinNumberToName[i], newState));
                    }
                }
            }
        }

        private void UpdateOutputPinStates(uint pinStates)
        {
            for (int i = 0; i < OutputPins.Count; i++)
            {
                bool newState = (pinStates & currentOutputMasks[i]) != 0;
                if (OutputPins[i].State != newState)
                {
                    OutputPins[i].State = newState;
                    if (outputPinNumberToName.ContainsKey(i))
                    {
                        OutputStateChanged?.Invoke(this, (outputPinNumberToName[i], newState));
                    }
                }
            }
        }

        private void RaiseError(string message)
        {
            Error?.Invoke(this, message);
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            Disconnect();
            cancellationTokenSource.Dispose();
        }
    }

    public class PinStatus : INotifyPropertyChanged
    {
        private int pinNumber;
        private string name;
        private bool state;

        public int PinNumber
        {
            get => pinNumber;
            set
            {
                pinNumber = value;
                OnPropertyChanged(nameof(PinNumber));
            }
        }

        public string Name
        {
            get => name;
            set
            {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public bool State
        {
            get => state;
            set
            {
                state = value;
                OnPropertyChanged(nameof(State));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}