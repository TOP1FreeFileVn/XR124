using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// MonoBehaviour listener cho GameEvent.
/// Kéo GameEvent asset vào, chọn action trong UnityEvent.
/// Tự đăng ký/hủy trong OnEnable/OnDisable.
/// </summary>
public class GameEventListener : MonoBehaviour, IGameEventListener
{
    public GameEvent gameEvent;
    public UnityEvent onRaised;

    void OnEnable()
    {
        if (gameEvent != null)
            gameEvent.Register(this);
    }

    void OnDisable()
    {
        if (gameEvent != null)
            gameEvent.Unregister(this);
    }

    public void OnEventRaised()
    {
        onRaised.Invoke();
    }
}
