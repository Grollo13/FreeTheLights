using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FreeTheLights.CarData;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FreeTheLights.Tests
{
    public class CarDataTests
    {
        private static readonly LedColor Red = LedColor.Parse("#FFFF0000");
        private static readonly LedColor Blue = LedColor.Parse("#FF0000FF");
        private static readonly LedColor Green = LedColor.Parse("#FF00FF00");

        private static CarLedProfile LoadFixture(string name)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
            return CarLedProfile.Parse(File.ReadAllText(path));
        }

        [Theory]
        [InlineData("AIX Racing 24", "aix-racing-24")]
        [InlineData("F12024", "f12024")]
        [InlineData("AssettoCorsaCompetizione", "assettocorsacompetizione")]
        [InlineData("Porsche 911 GT3 R (992) Évo", "porsche-911-gt3-r-992-evo")]
        [InlineData("--Already--hyphenated--", "already-hyphenated")]
        [InlineData("rss_formula_hybrid_2023_AC", "rss-formula-hybrid-2023-ac")]
        public void Normalizes_names_like_lovely_car_data(string input, string expected)
        {
            Assert.Equal(expected, NameNormalizer.Normalize(input));
        }

        [Fact]
        public void Manifest_lookup_uses_car_id_not_file_name()
        {
            var manifest = JObject.Parse(@"{ ""cars"": { ""assettocorsa"": [
                { ""carName"": ""RSS Formula Hybrid 2023"", ""carId"": ""rss_formula_hybrid_2023_AC"", ""path"": ""assettocorsa/rss-formula-hybrid-2023.json"" } ] } }");

            Assert.Equal("assettocorsa/rss-formula-hybrid-2023.json", CarDataService.FindInManifest(manifest, "assettocorsa", "rss_formula_hybrid_2023_AC"));
            Assert.Equal("assettocorsa/rss-formula-hybrid-2023.json", CarDataService.FindInManifest(manifest, "assettocorsa", "RSS_FORMULA_HYBRID_2023_ac"));
            Assert.Null(CarDataService.FindInManifest(manifest, "assettocorsa", "unknown_car"));
            Assert.Null(CarDataService.FindInManifest(manifest, "iracing", "rss_formula_hybrid_2023_AC"));
        }

        [Fact]
        public void Parses_formula_car_with_unused_leds()
        {
            var profile = LoadFixture("rss-formula-hybrid-2023.json");

            Assert.Equal("RSS Formula Hybrid 2023", profile.CarName);
            Assert.Equal(15, profile.LedCount);
            Assert.Equal(0, profile.RedlineBlinkIntervalMs);
            Assert.True(profile.Colors[0].IsTransparent);
            Assert.Equal(Red, profile.Colors[6]);
            Assert.Equal(Blue, profile.Colors[15]);
            Assert.Equal(12400, profile.GetThresholds("3")[0]);
        }

        [Fact]
        public void Formula_car_lights_red_then_blue_and_never_the_unused_leds()
        {
            var profile = LoadFixture("rss-formula-hybrid-2023.json");

            var low = profile.ComputeLeds(11000, "3", 0);
            Assert.All(low, c => Assert.True(c.IsTransparent));

            var mid = profile.ComputeLeds(11400, "3", 0);
            Assert.All(mid.Take(5), c => Assert.True(c.IsTransparent));
            Assert.All(mid.Skip(5).Take(5), c => Assert.Equal(Red, c));
            Assert.All(mid.Skip(10), c => Assert.True(c.IsTransparent));

            // Redline color is transparent and blink interval 0: the redline just keeps everything lit.
            var redline = profile.ComputeLeds(12500, "3", 0);
            Assert.All(redline.Take(5), c => Assert.True(c.IsTransparent));
            Assert.All(redline.Skip(10), c => Assert.Equal(Blue, c));
        }

        [Fact]
        public void Gt4_fills_from_both_ends_and_blinks_at_redline()
        {
            var profile = LoadFixture("alpine-a110-gt4.json");
            Assert.Equal(10, profile.LedCount);
            Assert.Equal(300, profile.RedlineBlinkIntervalMs);

            var first = profile.ComputeLeds(5600, "4", 0);
            Assert.Equal(Green, first[0]);
            Assert.Equal(Green, first[9]);
            Assert.All(first.Skip(1).Take(8), c => Assert.True(c.IsTransparent));

            var on = profile.ComputeLeds(6500, "4", 0);
            Assert.All(on, c => Assert.False(c.IsTransparent));

            var off = profile.ComputeLeds(6500, "4", 300);
            Assert.All(off, c => Assert.Equal(LedColor.Off, c));
        }

        [Fact]
        public void Redline_blink_off_phase_darkens_only_lit_leds()
        {
            // The second LED has a transparent color (unused): it stays transparent in both phases.
            var json = @"{ ""ledNumber"": 3, ""ledColor"": [""#00000000"", ""lime"", ""#00000000"", ""red""],
                ""ledRpm"": [ { ""1"": [8000, 5000, 6000, 7000] } ], ""redlineBlinkInterval"": 100 }";
            var profile = CarLedProfile.Parse(json);

            Assert.Equal(new[] { LedColor.Parse("lime"), LedColor.Transparent, LedColor.Parse("red") }, profile.ComputeLeds(8100, "1", 0));
            Assert.Equal(new[] { LedColor.Off, LedColor.Transparent, LedColor.Off }, profile.ComputeLeds(8100, "1", 100));
        }

        [Fact]
        public void Gt4_first_stage_shows_on_a_small_device()
        {
            var profile = LoadFixture("alpine-a110-gt4.json");
            var leds = LedResampler.ResampleLeds(profile.ComputeLeds(5600, "4", 0), 5);
            Assert.Equal(Green, leds[0]);
            Assert.Equal(Green, leds[4]);
        }

        [Fact]
        public void Unknown_gears_fall_back_to_nearest_gear_or_first_gear()
        {
            var json = @"{ ""ledNumber"": 2, ""ledColor"": [""#00000000"", ""red"", ""red""],
                ""ledRpm"": [ { ""1"": [9000, 1000, 2000], ""4"": [9000, 4000, 5000], ""R"": [9000, 100, 200] } ] }";
            var profile = CarLedProfile.Parse(json);

            Assert.Equal(4000, profile.GetThresholds("4")[1]);
            Assert.Equal(4000, profile.GetThresholds("7")[1]);
            Assert.Equal(1000, profile.GetThresholds("2")[1]);
            Assert.Equal(100, profile.GetThresholds("r")[1]);
            Assert.Equal(1000, profile.GetThresholds("N")[1]);
            Assert.Equal(1000, profile.GetThresholds(null)[1]);
        }

        [Fact]
        public void Redline_color_overrides_all_leds()
        {
            var json = @"{ ""ledNumber"": 3, ""ledColor"": [""#FFFFFFFF"", ""lime"", ""yellow"", ""red""],
                ""ledRpm"": [ { ""1"": [8000, 5000, 6000, 7000] } ] }";
            var profile = CarLedProfile.Parse(json);

            var leds = profile.ComputeLeds(8100, "1", 0);
            Assert.All(leds, c => Assert.Equal(LedColor.Parse("#FFFFFFFF"), c));
        }

        [Fact]
        public void Gaps_in_the_car_row_stay_dark()
        {
            // The McLaren 720S GT3 has 12 LEDs with gaps at LEDs 3 and 10 (transparent color, RPM 0).
            var profile = LoadFixture("mclaren720sgt3.json");
            var gaps = new[] { 2, 9 };

            var full = profile.ComputeLeds(7200, "1", 0);
            Assert.All(gaps, i => Assert.True(full[i].IsTransparent));
            Assert.Equal(8, full.Count(c => !c.IsTransparent)); // all but the gaps and the two redline LEDs

            // The redline color replaces only the real LEDs.
            var redline = profile.ComputeLeds(7300, "1", 0);
            Assert.All(gaps, i => Assert.True(redline[i].IsTransparent));
            Assert.All(redline.Where((c, i) => !gaps.Contains(i)), c => Assert.Equal(Blue, c));

            // Centered on a 15-LED row (12 + a center LED, device LEDs 1..13): the gaps stay dark there, too.
            var row = LedResampler.ResampleLeds(redline, 15);
            Assert.Equal(new[] { 0, 3, 11, 14 }, Enumerable.Range(0, 15).Where(j => row[j].IsTransparent).ToArray());
        }

        [Fact]
        public void Short_arrays_are_padded()
        {
            var json = @"{ ""ledNumber"": 4, ""ledColor"": [""#00000000"", ""red""], ""ledRpm"": [ { ""1"": [0, 1000] } ] }";
            var profile = CarLedProfile.Parse(json);

            var leds = profile.ComputeLeds(5000, "1", 0);
            Assert.Equal(4, leds.Length);
            Assert.False(leds[0].IsTransparent);
            Assert.True(leds.Skip(1).All(c => c.IsTransparent));
        }

        [Fact]
        public void Led_count_of_a_car_file_is_capped()
        {
            var json = @"{ ""ledNumber"": 100000000, ""ledColor"": [""#00000000"", ""red""], ""ledRpm"": [ { ""1"": [0, 1000] } ] }";
            var profile = CarLedProfile.Parse(json);

            Assert.Equal(LedLimits.MaxLeds, profile.LedCount);
            Assert.Equal(LedLimits.MaxLeds, profile.ComputeLeds(5000, "1", 0).Length);
        }

        [Fact]
        public void Retry_delay_doubles_up_to_the_maximum()
        {
            var first = TimeSpan.FromSeconds(30);
            Assert.Equal(TimeSpan.FromSeconds(30), CarDataService.RetryDelay(first, 1));
            Assert.Equal(TimeSpan.FromSeconds(60), CarDataService.RetryDelay(first, 2));
            Assert.Equal(TimeSpan.FromSeconds(240), CarDataService.RetryDelay(first, 4));
            Assert.Equal(CarDataService.MaxRetryDelay, CarDataService.RetryDelay(first, 20));
            Assert.Equal(CarDataService.MaxRetryDelay, CarDataService.RetryDelay(first, int.MaxValue));
        }

        [Fact]
        public void A_failed_load_is_retried_for_the_same_car()
        {
            var failures = 0;
            using (var service = Offline(TimeSpan.Zero, message => { if (message.Contains("failed to load")) Interlocked.Increment(ref failures); }))
            {
                service.Request("IRacing", "car");
                WaitFor(() => Volatile.Read(ref failures) == 1);

                // The same car again (as on every frame): without the retry nothing would happen.
                service.Request("IRacing", "car");
                WaitFor(() => Volatile.Read(ref failures) == 2);
            }
        }

        [Fact]
        public void A_failed_load_waits_before_retrying()
        {
            using (var service = Offline(TimeSpan.FromMinutes(1), null))
            {
                service.Request("IRacing", "car");
                WaitFor(() => service.Status == CarDataStatus.Error);

                // A new load would set Loading right away.
                service.Request("IRacing", "car");
                Assert.Equal(CarDataStatus.Error, service.Status);
            }
        }

        [Fact]
        public void Reload_loads_the_current_car_again()
        {
            var failures = 0;
            using (var service = Offline(TimeSpan.FromMinutes(1), message => { if (message.Contains("failed to load")) Interlocked.Increment(ref failures); }))
            {
                // A car id with the character that once separated game and car in the request key.
                service.Request("IRacing", "car|variant");
                WaitFor(() => Volatile.Read(ref failures) == 1);

                service.Reload();
                WaitFor(() => Volatile.Read(ref failures) == 2);
                Assert.Equal("car|variant", service.CarId);
                Assert.Equal("iracing", service.SimId);
            }
        }

        [Fact]
        public void A_car_file_without_rpm_data_is_not_retried()
        {
            var cache = Path.Combine(Path.GetTempPath(), "FreeTheLights-offline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(cache, "iracing"));
            File.WriteAllText(Path.Combine(cache, "iracing", "car.json"), @"{ ""carName"": ""Car without LEDs"" }");

            using (var service = Offline(TimeSpan.Zero, null, cache))
            {
                service.Request("IRacing", "car");
                WaitFor(() => service.Status == CarDataStatus.NotFound);

                // A retry would set Loading right away (the retry delay is zero).
                service.Request("IRacing", "car");
                Assert.Equal(CarDataStatus.NotFound, service.Status);
                Assert.Null(service.Current);
            }
        }

        /// <summary>
        /// A service whose downloads fail at once, like while offline (an unsupported URL scheme), with only what is in
        /// <paramref name="cache"/> (default: a new, empty folder) cached.
        /// </summary>
        private static CarDataService Offline(TimeSpan retryDelay, Action<string> log, string cache = null)
        {
            cache = cache ?? Path.Combine(Path.GetTempPath(), "FreeTheLights-offline-" + Guid.NewGuid().ToString("N"));
            return new CarDataService(cache, () => "ftp://example.invalid/data/", () => TimeSpan.FromHours(24), log, retryDelay);
        }

        private static void WaitFor(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the car data service");
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void Cache_paths_stay_inside_the_cache_folder()
        {
            var root = Path.Combine(Path.GetTempPath(), "FreeTheLights-cache");
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "iracing", "car.json")), CarDataService.ResolveCachePath(root, "iracing/car.json"));
            Assert.Null(CarDataService.ResolveCachePath(root, "../outside.json"));
            Assert.Null(CarDataService.ResolveCachePath(root, "iracing/../../outside.json"));
            Assert.Null(CarDataService.ResolveCachePath(root, "C:/Windows/win.ini"));
            Assert.Null(CarDataService.ResolveCachePath(root, ""));
            Assert.Null(CarDataService.ResolveCachePath(root, "bad\0name.json"));
        }

        [Theory]
        [InlineData("{ \"carName\": \"x\" }", true)]
        [InlineData("<html><body>Login</body></html>", false)]
        [InlineData("[1, 2]", false)]
        [InlineData("{ \"carName\": ", false)]
        [InlineData("", false)]
        public void Only_json_objects_are_cached(string content, bool expected)
        {
            Assert.Equal(expected, CarDataService.IsJsonObject(content));
        }
    }
}
