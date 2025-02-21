using System;
using System.Collections.Generic;
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
            if (slides.TryGetValue(name, out var slide))
                return slide;
            throw new ArgumentException($"Slide {name} not found");
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
