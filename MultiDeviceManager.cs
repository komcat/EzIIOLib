using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EzIIOLib
{
    public class MultiDeviceManager : IDisposable
    {
        private Dictionary<string, EzIIOManager> deviceManagers = new Dictionary<string, EzIIOManager>();

        public void AddDevice(string deviceName)
        {
            if (!deviceManagers.ContainsKey(deviceName))
            {
                deviceManagers[deviceName] = EzIIOManager.CreateFromConfig(deviceName);
            }
        }

        public EzIIOManager GetDevice(string deviceName)
        {
            if (deviceManagers.TryGetValue(deviceName, out var manager))
            {
                return manager;
            }
            throw new ArgumentException($"Device {deviceName} not found");
        }

        public void ConnectAll()
        {
            foreach (var manager in deviceManagers.Values)
            {
                manager.Connect();
            }
        }

        public void DisconnectAll()
        {
            foreach (var manager in deviceManagers.Values)
            {
                manager.Disconnect();
            }
        }
        public bool AreAllDevicesConnected()
        {
            // If no devices are registered, return false
            if (!deviceManagers.Any())
                return false;

            // Check if all devices are connected
            return deviceManagers.Values.All(manager => manager.IsConnected);
        }
        /// <summary>
        /// Get the input state of a specific pin on a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <param name="pinName">Name of the input pin</param>
        /// <returns>Boolean state of the pin, or null if not found</returns>
        public bool? GetInputState(string deviceName, string pinName)
        {
            var device = GetDevice(deviceName);
            return device.GetInputState(pinName);
        }

        /// <summary>
        /// Get the output state of a specific pin on a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>Boolean state of the pin, or null if not found</returns>
        public bool? GetOutputState(string deviceName, string pinName)
        {
            var device = GetDevice(deviceName);
            return device.GetOutputState(pinName);
        }

        /// <summary>
        /// Set (turn on) a specific output pin on a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool SetOutput(string deviceName, string pinName)
        {
            var device = GetDevice(deviceName);
            return device.SetOutput(pinName);
        }

        /// <summary>
        /// Clear (turn off) a specific output pin on a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearOutput(string deviceName, string pinName)
        {
            var device = GetDevice(deviceName);
            return device.ClearOutput(pinName);
        }

        /// <summary>
        /// Toggle the state of a specific output pin on a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool ToggleOutput(string deviceName, string pinName)
        {
            var device = GetDevice(deviceName);
            var currentState = device.GetOutputState(pinName);

            if (currentState.HasValue)
            {
                return currentState.Value
                    ? device.ClearOutput(pinName)
                    : device.SetOutput(pinName);
            }

            return false;
        }

        /// <summary>
        /// Get all input pins for a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOTop")</param>
        /// <returns>ObservableCollection of input pin statuses</returns>
        public ObservableCollection<PinStatus> GetInputPins(string deviceName)
        {
            var device = GetDevice(deviceName);
            return device.InputPins;
        }

        /// <summary>
        /// Get all output pins for a specific device
        /// </summary>
        /// <param name="deviceName">Name of the device (e.g., "IOBottom")</param>
        /// <returns>ObservableCollection of output pin statuses</returns>
        public ObservableCollection<PinStatus> GetOutputPins(string deviceName)
        {
            var device = GetDevice(deviceName);
            return device.OutputPins;
        }

        /// <summary>
        /// Set (turn on) an output pin using just the pin name. 
        /// Assumes pin names are unique across all devices.
        /// </summary>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool SetOutput(string pinName)
        {
            foreach (var deviceManager in deviceManagers.Values)
            {
                var pin = deviceManager.OutputPins.FirstOrDefault(p => p.Name == pinName);
                if (pin != null)
                {
                    return deviceManager.SetOutput(pinName);
                }
            }
            return false;
        }

        /// <summary>
        /// Clear (turn off) an output pin using just the pin name.
        /// Assumes pin names are unique across all devices.
        /// </summary>
        /// <param name="pinName">Name of the output pin</param>
        /// <returns>True if successful, false otherwise</returns>
        public bool ClearOutput(string pinName)
        {
            foreach (var deviceManager in deviceManagers.Values)
            {
                var pin = deviceManager.OutputPins.FirstOrDefault(p => p.Name == pinName);
                if (pin != null)
                {
                    return deviceManager.ClearOutput(pinName);
                }
            }
            return false;
        }
        /// <summary>
        /// Get the state of an input pin using just the pin name.
        /// Assumes pin names are unique across all devices.
        /// </summary>
        /// <param name="pinName">Name of the input pin</param>
        /// <returns>Boolean state of the pin, or null if pin not found</returns>
        public bool? GetInputState(string pinName)
        {
            foreach (var deviceManager in deviceManagers.Values)
            {
                var pin = deviceManager.InputPins.FirstOrDefault(p => p.Name == pinName);
                if (pin != null)
                {
                    return deviceManager.GetInputState(pinName);
                }
            }
            return null;
        }
        public void Dispose()
        {
            foreach (var manager in deviceManagers.Values)
            {
                manager.Dispose();
            }
            deviceManagers.Clear();
        }
    }
}
