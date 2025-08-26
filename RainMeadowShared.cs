using System.Diagnostics;
using System.Net;

namespace RainMeadow.Shared {
    static partial class SharedPlatform
    {
        // Blackhole Endpoint
        // https://superuser.com/questions/698244/ip-address-that-is-the-equivalent-of-dev-null

        static partial void getHeartBeatTime(ref ulong heartbeatTime) => heartbeatTime = IndividualServer.IndividualServer.heartbeatTime;   
        static partial void getTimeoutTime(ref ulong TimeoutTime) => TimeoutTime = IndividualServer.IndividualServer.timeoutTime;   
        static partial void getTimeMS(ref ulong time) => time = (ulong)((Stopwatch.GetTimestamp()*1000)/Stopwatch.Frequency);   
    }
}
