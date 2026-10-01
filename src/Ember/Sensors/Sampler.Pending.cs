using Ember.Core.Config;
using Ember.Core.Status;

namespace Ember.Sensors;

// Sources that are not implemented yet.
internal sealed partial class Sampler
{
    private void UpdateGpuCounters(EmberSettings cfg) { }
    private void UpdateMemDiskCounters(EmberSettings cfg) { }
    private static bool AnyGpuItem(EmberSettings cfg) => false;
    private List<GpuData>? ReadGpus(EmberSettings cfg) => null;
    private MemData? ReadMemory(EmberSettings cfg, bool pdhOk) => null;
    private List<DiskData>? ReadDisks(EmberSettings cfg) => null;
    private static List<VolumeData>? ReadVolumes() => null;
}
