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
        private readonly EzIIOManager outputDevice;
        private readonly EzIIOManager inputDevice;
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

        public PneumaticSlide(PneumaticSlideConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            // Create IO device managers
            outputDevice = EzIIOManager.CreateFromConfig(config.Output.DeviceName);

            // Check if inputs are on the same device as outputs
            if (config.ExtendedInput.DeviceName == config.Output.DeviceName)
                inputDevice = outputDevice;
            else
                inputDevice = EzIIOManager.CreateFromConfig(config.ExtendedInput.DeviceName);

            // Subscribe to input state changes
            inputDevice.InputStateChanged += OnInputStateChanged;

            // Connect devices
            if (!outputDevice.Connect())
                throw new Exception($"Failed to connect to output device {config.Output.DeviceName}");

            if (inputDevice != outputDevice && !inputDevice.Connect())
                throw new Exception($"Failed to connect to input device {config.ExtendedInput.DeviceName}");

            // Initialize position based on current sensor states
            UpdatePosition();
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
                ExtendedSensor = inputDevice.GetInputState(config.ExtendedInput.PinName) ?? false,
                RetractedSensor = inputDevice.GetInputState(config.RetractedInput.PinName) ?? false
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
                    outputDevice.SetOutput(config.Output.PinName);
                else
                    outputDevice.ClearOutput(config.Output.PinName);

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
            var extendedState = inputDevice.GetInputState(config.ExtendedInput.PinName);
            var retractedState = inputDevice.GetInputState(config.RetractedInput.PinName);

            SlidePosition newPosition;

            if (extendedState == true && retractedState == false)
                newPosition = SlidePosition.Extended;
            else if (extendedState == false && retractedState == true)
                newPosition = SlidePosition.Retracted;
            else if (extendedState == false && retractedState == false)
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

                if (inputDevice != outputDevice)
                    inputDevice?.Dispose();

                outputDevice?.Dispose();
            }
        }
    }
}