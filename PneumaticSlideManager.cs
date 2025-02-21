using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EzIIOLib
{
    


    public class PneumaticSlideManager
    {
        private readonly MultiDeviceManager deviceManager;
        private readonly Dictionary<string, PneumaticSlide> slides = new Dictionary<string, PneumaticSlide>();

        public PneumaticSlideManager(MultiDeviceManager deviceManager)
        {
            this.deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
        }

        public void LoadSlidesFromConfig(IOConfiguration config)
        {
            foreach (var slideConfig in config.PneumaticSlides)
            {
                AddSlide(slideConfig);
            }
        }

        public void AddSlide(PneumaticSlideConfig config)
        {
            if (slides.ContainsKey(config.Name))
                throw new ArgumentException($"Slide {config.Name} already exists");

            var slide = new PneumaticSlide(deviceManager, config);
            slides[config.Name] = slide;
        }

        public PneumaticSlide GetSlide(string name)
        {
            System.Diagnostics.Debug.WriteLine($"[PneumaticSlideManager] Attempting to get slide: '{name}'");
            System.Diagnostics.Debug.WriteLine($"[PneumaticSlideManager] Current slides in dictionary: {slides.Count}");

            // Log all available slide names
            System.Diagnostics.Debug.WriteLine("[PneumaticSlideManager] Available slides:");
            foreach (var slideName in slides.Keys)
            {
                System.Diagnostics.Debug.WriteLine($"  - '{slideName}'");
            }

            if (slides.TryGetValue(name, out var slide))
            {
                System.Diagnostics.Debug.WriteLine($"[PneumaticSlideManager] Found slide: '{name}'");
                return slide;
            }

            System.Diagnostics.Debug.WriteLine($"[PneumaticSlideManager] ERROR: Slide '{name}' not found!");
            throw new ArgumentException($"Slide '{name}' not found. Available slides: {string.Join(", ", slides.Keys)}");
        }

        public void Dispose()
        {
            foreach (var slide in slides.Values)
            {
                slide.Dispose();
            }
            slides.Clear();
        }
    }
}
