namespace Loupedeck.HapticGamingBridgePlugin
{
    using System;

    // Required by the plugin loader even for plugins with no target application.
    public class HapticGamingBridgeApplication : ClientApplication
    {
        protected override String GetProcessName() => "";

        protected override String GetBundleName() => "";

        public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
    }
}
