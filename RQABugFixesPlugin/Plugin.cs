using NLog;
using Torch;

namespace RQABugFixes;

public class Plugin : TorchPluginBase
{
    internal static readonly Logger Log = LogManager.GetLogger("RQABugFixes");
}
