using SystemPerformanceAccelerator.Core.Models;

namespace SystemPerformanceAccelerator.Core.Interfaces;

public interface IDevelopmentEditionOverrideProvider
{
    ApplicationEdition? GetOverride();
}
