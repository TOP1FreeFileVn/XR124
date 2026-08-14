using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject-based game event.
/// Nhẹ, zero allocation, không phụ thuộc MonoBehaviour.
/// </summary>
[CreateAssetMenu(fileName = "GameEvent_", menuName = "XR124/Game Event", order = 1)]
public class GameEvent : ScriptableObject
{
#if UNITY_EDITOR
    [TextArea(1, 3)]
    public string description;
#endif

    // Dùng non-generic list — listener tự quản lý
    private readonly List<IGameEventListener> listeners = new List<IGameEventListener>(8);

    public void Raise()
    {
        for (int i = listeners.Count - 1; i >= 0; i--)
            listeners[i].OnEventRaised();
    }

    public void Register(IGameEventListener listener)
    {
        if (!listeners.Contains(listener))
            listeners.Add(listener);
    }

    public void Unregister(IGameEventListener listener)
    {
        listeners.Remove(listener);
    }
}

/// <summary>
/// Interface cho listener — tránh boxing/generic trong event system.
/// </summary>
public interface IGameEventListener
{
    void OnEventRaised();
}
