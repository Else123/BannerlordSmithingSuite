using SmithingSuite.Logic;
using Xunit;

namespace SmithingSuite.Tests
{
    public class LogMarkersTests
    {
        [Fact]
        public void FormatError_carries_the_error_tag_and_message()
        {
            string line = LogMarkers.FormatError("12:34:56.789", "boom");

            Assert.Equal("[12:34:56.789] [ERROR] boom", line);
            Assert.Contains(LogMarkers.ErrorTag, line);
        }

        [Fact]
        public void ErrorTag_is_the_stable_marker_scripts_search_for()
        {
            Assert.Equal("[ERROR]", LogMarkers.ErrorTag);
        }
    }
}
