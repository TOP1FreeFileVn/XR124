using System;
using System.Collections;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class RoomSceneSetup : MonoBehaviour
{
    [Header("Room Loading")]
    public bool reloadRoomAfterScan = true;

    [Header("Runtime Debug")]
    [SerializeField] private bool roomReady;
    [SerializeField] private bool scanInProgress;
    [SerializeField] private string lastStatus = "Waiting for MRUK";

    private bool subscribed;

    private IEnumerator Start()
    {
        while (MRUK.Instance == null)
        {
            yield return null;
        }

        SubscribeToMRUK();
        if (MRUK.Instance.IsInitialized)
        {
            HandleSceneLoaded();
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromMRUK();
    }

    // Mo Space Setup cua Quest. Khi nguoi dung xac nhan, he thong tu luu room tren kinh.
    public async void ScanAndSaveRoom()
    {
        if (scanInProgress)
        {
            return;
        }

        scanInProgress = true;
        lastStatus = "Opening Quest Space Setup";

        try
        {
            bool captured = await OVRScene.RequestSpaceSetup();
            if (!captured)
            {
                lastStatus = "Space Setup was cancelled or failed";
                return;
            }

            lastStatus = "Room saved by Quest";
            if (reloadRoomAfterScan && MRUK.Instance != null)
            {
                MRUK.LoadDeviceResult result =
                    await MRUK.Instance.LoadSceneFromDevice(false);
                lastStatus = "Reload room: " + result;
            }
        }
        catch (Exception exception)
        {
            lastStatus = "Room scan failed: " + exception.Message;
            Debug.LogException(exception, this);
        }
        finally
        {
            scanInProgress = false;
        }
    }

    // Tai lai room da duoc Quest luu ma khong mo man hinh quet moi.
    public async void LoadSavedRoom()
    {
        if (MRUK.Instance == null || scanInProgress)
        {
            return;
        }

        scanInProgress = true;
        lastStatus = "Loading saved room";

        try
        {
            MRUK.LoadDeviceResult result =
                await MRUK.Instance.LoadSceneFromDevice(false);
            lastStatus = "Load room: " + result;
        }
        catch (Exception exception)
        {
            lastStatus = "Load room failed: " + exception.Message;
            Debug.LogException(exception, this);
        }
        finally
        {
            scanInProgress = false;
        }
    }

    private void SubscribeToMRUK()
    {
        if (subscribed || MRUK.Instance == null)
        {
            return;
        }

        MRUK.Instance.SceneLoadedEvent.AddListener(HandleSceneLoaded);
        subscribed = true;
    }

    private void UnsubscribeFromMRUK()
    {
        if (!subscribed || MRUK.Instance == null)
        {
            return;
        }

        MRUK.Instance.SceneLoadedEvent.RemoveListener(HandleSceneLoaded);
        subscribed = false;
    }

    private void HandleSceneLoaded()
    {
        MRUKRoom room = MRUK.Instance != null && MRUK.Instance.Rooms.Count > 0
            ? MRUK.Instance.Rooms[0]
            : null;
        roomReady = room != null && room.FloorAnchors.Count > 0;
        lastStatus = roomReady
            ? "Room ready with floor"
            : "Room loaded without a floor";
    }
}
