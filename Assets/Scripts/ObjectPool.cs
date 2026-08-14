using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Object pool dùng mảng tĩnh + stack pointer — ZERO allocation sau khởi tạo.
/// Không dùng Queue (sinh GC), không Instantiate/Destroy runtime.
/// </summary>
public class ObjectPool : MonoBehaviour
{
    [Header("Pool Config")]
    public GameObject prefab;
    public int initialSize = 10;
    public int maxSize = 50;

    [Header("Debug")]
    public int activeCount;
    public int availableCount;

    private GameObject[] pool;
    private int top; // stack pointer: pool[0..top-1] = available

    void Awake()
    {
        if (prefab == null)
        {
            Debug.LogError($"[ObjectPool] {name}: prefab is null", this);
            return;
        }

        pool = new GameObject[maxSize];
        Prewarm(initialSize);
    }

    /// <summary>Khởi tạo sẵn N object</summary>
    public void Prewarm(int count)
    {
        int toCreate = Mathf.Min(count, maxSize) - top;
        for (int i = 0; i < toCreate; i++)
        {
            GameObject obj = Instantiate(prefab, transform);
            obj.SetActive(false);
            pool[top++] = obj;
        }
    }

    /// <summary>Lấy object từ pool. Trả về null nếu pool cạn và không thể mở rộng.</summary>
    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        if (top == 0)
        {
            if (activeCount + availableCount >= maxSize)
            {
                Debug.LogWarning($"[ObjectPool] {name}: pool exhausted ({maxSize})", this);
                return null;
            }
            // Mở rộng: thêm 1 object
            GameObject newObj = Instantiate(prefab, transform);
            pool[top++] = newObj;
        }

        GameObject obj = pool[--top];
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        activeCount++;
        availableCount = top;

        return obj;
    }

    /// <summary>Lấy object, dùng transform của parent làm vị trí</summary>
    public GameObject Get()
    {
        return Get(transform.position, transform.rotation);
    }

    /// <summary>Trả object về pool</summary>
    public void Return(GameObject obj)
    {
        if (obj == null) return;

        obj.SetActive(false);
        obj.transform.SetParent(transform);

        if (top < pool.Length)
        {
            pool[top++] = obj;
            activeCount--;
            availableCount = top;
        }
        else
        {
            // Pool đầy bất thường → destroy để tránh leak
            Destroy(obj);
        }
    }

    /// <summary>Trả tất cả object đang active về pool</summary>
    public void ReturnAll()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.gameObject.activeSelf)
                Return(child.gameObject);
        }
    }

    /// <summary>Kiểm tra còn object可用 không</summary>
    public bool HasAvailable()
    {
        return top > 0;
    }
}
