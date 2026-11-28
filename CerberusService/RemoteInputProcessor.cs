using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoomAgent
{
    public interface IRemoteInputSender
    {
        bool MoveMouseRelative(int dx, int dy);
        bool SendMouseButton(string button, string action);
        bool SendMouseScroll(int deltaX, int deltaY);
        bool SendKey(ushort vk, string action);
        bool SendKeyCombo(ushort[] vks);
        bool SendUnicodeText(string text);
    }

    internal sealed class WindowsRemoteInputSender : IRemoteInputSender
    {
        public bool MoveMouseRelative(int dx, int dy) { return NativeInterop.MoveMouseRelative(dx, dy); }
        public bool SendMouseButton(string button, string action) { return NativeInterop.SendMouseButton(button, action); }
        public bool SendMouseScroll(int deltaX, int deltaY) { return NativeInterop.SendMouseScroll(deltaX, deltaY); }
        public bool SendKey(ushort vk, string action) { return NativeInterop.SendKey(vk, action); }
        public bool SendKeyCombo(ushort[] vks) { return NativeInterop.SendKeyCombo(vks); }
        public bool SendUnicodeText(string text) { return NativeInterop.SendUnicodeText(text); }
    }

    public static class RemoteInputProcessor
    {
        private const int MaxDelta = 2000;
        private const int MaxScrollDelta = 2400;
        private const int MaxComboLength = 8;
        private const int MaxTextLength = 512;

        public static bool ProcessJsonMessage(string json, IRemoteInputSender sender, out string error)
        {
            error = null;
            if (sender == null)
            {
                error = "missing_sender";
                return false;
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "empty_message";
                return false;
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        error = "invalid_payload";
                        return false;
                    }

                    JsonElement root = doc.RootElement;
                    string type = GetString(root, "t") ?? GetString(root, "type");
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        error = "missing_type";
                        return false;
                    }

                    string normalizedType = type.Trim().ToLowerInvariant();
                    switch (normalizedType)
                    {
                        case "move":
                        {
                            int dx = Clamp(GetInt(root, "dx", 0), -MaxDelta, MaxDelta);
                            int dy = Clamp(GetInt(root, "dy", 0), -MaxDelta, MaxDelta);
                            return sender.MoveMouseRelative(dx, dy);
                        }
                        case "scroll":
                        {
                            int dx = Clamp(GetInt(root, "dx", 0), -MaxScrollDelta, MaxScrollDelta);
                            int dy = Clamp(GetInt(root, "dy", 0), -MaxScrollDelta, MaxScrollDelta);
                            return sender.SendMouseScroll(dx, dy);
                        }
                        case "btn":
                        case "button":
                        {
                            string button = (GetString(root, "b") ?? GetString(root, "button") ?? "left").Trim().ToLowerInvariant();
                            if (button != "left" && button != "right" && button != "middle")
                            {
                                error = "invalid_button";
                                return false;
                            }
                            string action = (GetString(root, "a") ?? GetString(root, "action") ?? "click").Trim().ToLowerInvariant();
                            if (action != "down" && action != "up" && action != "click")
                            {
                                error = "invalid_button_action";
                                return false;
                            }
                            return sender.SendMouseButton(button, action);
                        }
                        case "key":
                        {
                            int vkInt = GetInt(root, "vk", 0);
                            if (vkInt <= 0 || vkInt > 0xFF)
                            {
                                error = "invalid_vk";
                                return false;
                            }
                            string action = (GetString(root, "a") ?? GetString(root, "action") ?? "press").Trim().ToLowerInvariant();
                            if (action != "down" && action != "up" && action != "press")
                            {
                                error = "invalid_key_action";
                                return false;
                            }
                            return sender.SendKey((ushort)vkInt, action);
                        }
                        case "combo":
                        {
                            JsonElement vksElem;
                            if (!root.TryGetProperty("vks", out vksElem) || vksElem.ValueKind != JsonValueKind.Array)
                            {
                                error = "missing_vks";
                                return false;
                            }
                            List<ushort> vks = new List<ushort>();
                            foreach (JsonElement item in vksElem.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Number) continue;
                                int vk = item.GetInt32();
                                if (vk > 0 && vk <= 0xFF)
                                {
                                    vks.Add((ushort)vk);
                                    if (vks.Count >= MaxComboLength) break;
                                }
                            }
                            if (vks.Count == 0)
                            {
                                error = "empty_vks";
                                return false;
                            }
                            return sender.SendKeyCombo(vks.ToArray());
                        }
                        case "text":
                        {
                            string text = GetString(root, "s") ?? GetString(root, "text") ?? "";
                            if (text.Length > MaxTextLength)
                            {
                                text = text.Substring(0, MaxTextLength);
                            }
                            return sender.SendUnicodeText(text);
                        }
                        default:
                            error = "unknown_type";
                            return false;
                    }
                }
            }
            catch (JsonException)
            {
                error = "invalid_json";
                return false;
            }
        }

        private static string GetString(JsonElement obj, string name)
        {
            JsonElement prop;
            if (obj.TryGetProperty(name, out prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return null;
        }

        private static int GetInt(JsonElement obj, string name, int fallback)
        {
            JsonElement prop;
            if (obj.TryGetProperty(name, out prop) && prop.ValueKind == JsonValueKind.Number)
            {
                double val;
                if (prop.TryGetDouble(out val) && !double.IsNaN(val) && !double.IsInfinity(val))
                {
                    return (int)Math.Round(val);
                }
            }
            return fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
