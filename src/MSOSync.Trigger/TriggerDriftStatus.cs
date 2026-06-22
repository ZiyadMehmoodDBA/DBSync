// src/MSOSync.Trigger/TriggerDriftStatus.cs
using System.Text.Json.Serialization;

namespace MSOSync.Trigger;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TriggerDriftStatus { Valid, Drift, Missing }
