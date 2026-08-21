using System;

namespace Gamma_Manager
{
    internal class InternalMonitor
    {
        //For built-in laptop monitors
        //Found somewhere, don't want to revise, it just works

        public static bool TryGetBrightness(out int value)
        {
            value = 0;
            try
            {
                System.Management.ManagementScope s = new System.Management.ManagementScope("root\\WMI");
                System.Management.SelectQuery q = new System.Management.SelectQuery("WmiMonitorBrightness");
                using (System.Management.ManagementObjectSearcher mos = new System.Management.ManagementObjectSearcher(s, q))
                using (System.Management.ManagementObjectCollection moc = mos.Get())
                {
                    foreach (System.Management.ManagementObject o in moc)
                    {
                        value = (byte)o.GetPropertyValue("CurrentBrightness");
                        return value >= 0 && value <= 100;
                    }
                }
            }
            catch { }
            return false;
        }

        public static int GetBrightness()
        {
            int value;
            return TryGetBrightness(out value) ? value : -1;
        }

        /*public static byte[] GetBrightnessLevels()
        {
            //define scope (namespace)
            System.Management.ManagementScope s = new System.Management.ManagementScope("root\\WMI");

            //define query
            System.Management.SelectQuery q = new System.Management.SelectQuery("WmiMonitorBrightness");

            //output current brightness
            System.Management.ManagementObjectSearcher mos = new System.Management.ManagementObjectSearcher(s, q);
            byte[] BrightnessLevels = new byte[0];

            try
            {
                System.Management.ManagementObjectCollection moc = mos.Get();

                //store result


                foreach (System.Management.ManagementObject o in moc)
                {
                    BrightnessLevels = (byte[])o.GetPropertyValue("Level");
                    break; //only work on the first object
                }

                moc.Dispose();
                mos.Dispose();

            }
            catch (Exception)
            {
                Console.WriteLine("Sorry, Your System does not support this brightness control...");
            }

            return BrightnessLevels;
        }*/

        public static void SetBrightness(byte targetBrightness)
        {
            //define scope (namespace)
            System.Management.ManagementScope s = new System.Management.ManagementScope("root\\WMI");

            //define query
            System.Management.SelectQuery q = new System.Management.SelectQuery("WmiMonitorBrightnessMethods");

            //output current brightness
            System.Management.ManagementObjectSearcher mos = new System.Management.ManagementObjectSearcher(s, q);

            System.Management.ManagementObjectCollection moc = mos.Get();

            foreach (System.Management.ManagementObject o in moc)
            {
                o.InvokeMethod("WmiSetBrightness", new Object[] { UInt32.MaxValue, targetBrightness }); //note the reversed order - won't work otherwise!
                break; //only work on the first object
            }

            moc.Dispose();
            mos.Dispose();
        }
    }
}
