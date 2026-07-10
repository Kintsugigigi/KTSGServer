using System;
using LiteNetLib;

#if !UNITY_5_3_OR_NEWER
using Serilog;
#endif

namespace KTSG.Network
{
    public class NetLogger : INetLogger
    {
        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            switch (level)
            {
                case NetLogLevel.Info:
                    Info(string.Format(str, args));
                    break;
                case NetLogLevel.Warning:
                    Warning(string.Format(str, args));
                    break;
                case NetLogLevel.Error:
                    Error(string.Format(str, args));
                    break;
                case NetLogLevel.Trace:
                    Debug(string.Format(str, args));
                    break;
            }
        }

        public static void Info(string text)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log(text);
#else
            Log.Information(text);
#endif
        }

        public static void Debug(string text)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log($"<color=#00FFFF>[Debug]</color> {text}");
#else
            Log.Debug(text);
#endif
        }

        public static void Warning(string text)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning(text);
#else
            Log.Warning(text);
#endif
        }

        public static void Error(string text)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError(text);
#else
            Log.Error(text);
#endif
        }

        public static void Error(Exception ex, string text)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError(text);
            UnityEngine.Debug.LogException(ex);
#else
            Log.Error(ex, text);
#endif
        }
    }
}
