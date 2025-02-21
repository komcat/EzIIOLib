using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace EzIIOLib
{
    // Update the main configuration class to include pneumatic slides
    public class IOConfiguration
    {
        public Metadata Metadata { get; set; }
        public List<PneumaticSlideConfig> PneumaticSlides { get; set; } = new List<PneumaticSlideConfig>();
        public List<EziioDevice> Eziio { get; set; }
    }
    public class SensorState
    {
        public bool ExtendedSensor { get; set; }
        public bool RetractedSensor { get; set; }
    }
    public class PneumaticSlideConfig
    {
        public string Name { get; set; }
        public OutputConfig Output { get; set; }
        public InputConfig ExtendedInput { get; set; }
        public InputConfig RetractedInput { get; set; }
        public int TimeoutMs { get; set; } = 5000; // Default timeout 5 seconds
    }

    public class OutputConfig
    {
        public string DeviceName { get; set; }
        public string PinName { get; set; }
    }

    public class InputConfig
    {
        public string DeviceName { get; set; }
        public string PinName { get; set; }
    }

    public enum SlidePosition
    {
        Unknown,
        Extended,
        Retracted,
        Moving
    }

    public class PneumaticSlide : IDisposable
    {
        private readonly MultiDeviceManager deviceManager;
        private readonly PneumaticSlideConfig config;
        private SlidePosition currentPosition = SlidePosition.Unknown;
        private TaskCompletionSource<bool> movementCompletion;
        private bool isDisposed;
        private bool isMoving;

        public event EventHandler<SlidePosition> PositionChanged;
        public event EventHandler<string> Error;
        public event EventHandler<SensorState> SensorStateChanged;

        public string Name => config.Name;
        public SlidePosition Position => currentPosition;
        public bool IsMoving => isMoving;

        public PneumaticSlide(MultiDeviceManager deviceManager, PneumaticSlideConfig config)
        {
            this.deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            // Subscribe to input state changes for both devices
            var outputDevice = deviceManager.GetDevice(config.Output.DeviceName);
            var inputDevice = deviceManager.GetDevice(config.ExtendedInput.DeviceName);

            // Add event subscription for input state changes
            inputDevice.InputStateChanged += OnInputStateChanged;

            // Initialize position based on current sensor states
            UpdatePosition();
        }

        private bool GetInputState(string deviceName, string pinName)
        {
            return deviceManager.GetInputState(deviceName, pinName) ?? false;
        }

        public async Task<bool> ExtendAsync()
        {
            if (isMoving)
                return false;

            if (currentPosition == SlidePosition.Extended)
                return true;

            return await MoveToPosition(true);
        }

        public async Task<bool> RetractAsync()
        {
            if (isMoving)
                return false;

            if (currentPosition == SlidePosition.Retracted)
                return true;

            return await MoveToPosition(false);
        }

        public SensorState GetSensorStates()
        {
            return new SensorState
            {
                ExtendedSensor = GetInputState(config.ExtendedInput.DeviceName, config.ExtendedInput.PinName),
                RetractedSensor = GetInputState(config.RetractedInput.DeviceName, config.RetractedInput.PinName)
            };
        }

        private async Task<bool> MoveToPosition(bool extend)
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(PneumaticSlide));

            if (isMoving)
                return false;

            isMoving = true;

            // Cancel any existing movement
            movementCompletion?.TrySetCanceled();
            movementCompletion = new TaskCompletionSource<bool>();

            try
            {
                currentPosition = SlidePosition.Moving;
                PositionChanged?.Invoke(this, currentPosition);

                // Set output to desired state
                if (extend)
                    deviceManager.SetOutput(config.Output.DeviceName, config.Output.PinName);
                else
                    deviceManager.ClearOutput(config.Output.DeviceName, config.Output.PinName);

                // Wait for movement to complete or timeout
                using (var cts = new System.Threading.CancellationTokenSource(config.TimeoutMs))
                {
                    cts.Token.Register(() => movementCompletion.TrySetResult(false));
                    bool success = await movementCompletion.Task;

                    if (!success)
                    {
                        Error?.Invoke(this, $"Movement timeout after {config.TimeoutMs}ms");
                        currentPosition = SlidePosition.Unknown;
                        PositionChanged?.Invoke(this, currentPosition);
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, $"Movement error: {ex.Message}");
                currentPosition = SlidePosition.Unknown;
                PositionChanged?.Invoke(this, currentPosition);
                return false;
            }
            finally
            {
                isMoving = false;
            }
        }

        private void OnInputStateChanged(object sender, (string PinName, bool State) e)
        {
            if (e.PinName == config.ExtendedInput.PinName || e.PinName == config.RetractedInput.PinName)
            {
                UpdatePosition();

                var state = GetSensorStates();
                SensorStateChanged?.Invoke(this, state);
            }
        }

        private void UpdatePosition()
        {
            var extendedState = GetInputState(config.ExtendedInput.DeviceName, config.ExtendedInput.PinName);
            var retractedState = GetInputState(config.RetractedInput.DeviceName, config.RetractedInput.PinName);

            SlidePosition newPosition;

            if (extendedState && !retractedState)
                newPosition = SlidePosition.Extended;
            else if (!extendedState && retractedState)
                newPosition = SlidePosition.Retracted;
            else if (!extendedState && !retractedState)
                newPosition = SlidePosition.Moving;
            else
                newPosition = SlidePosition.Unknown; // Both sensors active - error condition

            if (newPosition != currentPosition)
            {
                currentPosition = newPosition;
                PositionChanged?.Invoke(this, currentPosition);

                // Complete movement if we've reached a final position
                if ((newPosition == SlidePosition.Extended || newPosition == SlidePosition.Retracted)
                    && movementCompletion != null)
                {
                    movementCompletion.TrySetResult(true);
                }
            }
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
                movementCompletion?.TrySetCanceled();
            }
        }
    }
}