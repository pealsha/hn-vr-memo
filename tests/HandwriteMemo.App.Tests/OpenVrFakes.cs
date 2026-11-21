using Valve.VR;

namespace HandwriteMemo.App;

internal sealed class FakeVrSystem : IOpenVrSystem
{
    public bool GetTimeSinceLastVsync(ref float pfSecondsSinceLastVsync, ref ulong pulFrameCounter) => throw new NotSupportedException();
    public void GetDXGIOutputInfo(ref int pnAdapterIndex) => throw new NotSupportedException();
    public void GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin eOrigin, float fPredictedSecondsToPhotonsFromNow, TrackedDevicePose_t[] pTrackedDevicePoseArray) => throw new NotSupportedException();
    public uint GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole unDeviceType) => throw new NotSupportedException();
    public float GetFloatTrackedDeviceProperty(uint unDeviceIndex, ETrackedDeviceProperty prop, ref ETrackedPropertyError pError) => throw new NotSupportedException();
    public bool PollNextEvent(ref VREvent_t pEvent, uint uncbVREvent) => false;
}

internal sealed class FakeVrOverlay(Queue<VREvent_t>? events = null, ulong eventHandle = 0) : IOpenVrOverlay
{
    internal Action<ulong, float, float, float>? ColorChanged { get; init; }
    internal Action<ulong>? TextureSubmitted { get; init; }
    internal Action<ulong>? PoseUpdated { get; init; }

    public EVROverlayError CreateOverlay(string pchOverlayKey, string pchOverlayName, ref ulong pOverlayHandle) => throw new NotSupportedException();
    public EVROverlayError DestroyOverlay(ulong ulOverlayHandle) => throw new NotSupportedException();
    public EVROverlayError SetOverlayFlag(ulong ulOverlayHandle, VROverlayFlags eOverlayFlag, bool bEnabled) => throw new NotSupportedException();
    public EVROverlayError SetOverlayColor(ulong ulOverlayHandle, float fRed, float fGreen, float fBlue)
    {
        ColorChanged?.Invoke(ulOverlayHandle, fRed, fGreen, fBlue);
        return EVROverlayError.None;
    }
    public EVROverlayError SetOverlaySortOrder(ulong ulOverlayHandle, uint unSortOrder) => throw new NotSupportedException();
    public EVROverlayError SetOverlayWidthInMeters(ulong ulOverlayHandle, float fWidthInMeters) => EVROverlayError.None;
    public EVROverlayError SetOverlayTransformAbsolute(ulong ulOverlayHandle, ETrackingUniverseOrigin eTrackingOrigin, ref HmdMatrix34_t pmatTrackingOriginToOverlayTransform)
    {
        PoseUpdated?.Invoke(ulOverlayHandle);
        return EVROverlayError.None;
    }
    public EVROverlayError SetOverlayTransformTrackedDeviceRelative(ulong ulOverlayHandle, uint unTrackedDevice, ref HmdMatrix34_t pmatTrackedDeviceToOverlayTransform) => throw new NotSupportedException();
    public EVROverlayError ShowOverlay(ulong ulOverlayHandle) => EVROverlayError.None;
    public EVROverlayError HideOverlay(ulong ulOverlayHandle) => EVROverlayError.None;
    public EVROverlayError WaitFrameSync(uint nTimeoutMs) => throw new NotSupportedException();
    public bool PollNextOverlayEvent(ulong ulOverlayHandle, ref VREvent_t pEvent, uint uncbVREvent) => ulOverlayHandle == eventHandle && events is not null && events.TryDequeue(out pEvent);
    public EVROverlayError SetOverlayInputMethod(ulong ulOverlayHandle, VROverlayInputMethod eInputMethod) => throw new NotSupportedException();
    public EVROverlayError SetOverlayMouseScale(ulong ulOverlayHandle, ref HmdVector2_t pvecMouseScale) => throw new NotSupportedException();
    public EVROverlayError SetOverlayTexture(ulong ulOverlayHandle, ref Texture_t pTexture)
    {
        TextureSubmitted?.Invoke(ulOverlayHandle);
        return EVROverlayError.None;
    }
    public EVROverlayError ClearOverlayTexture(ulong ulOverlayHandle) => EVROverlayError.None;
    public EVROverlayError CreateDashboardOverlay(string pchOverlayKey, string pchOverlayFriendlyName, ref ulong pMainHandle, ref ulong pThumbnailHandle) => throw new NotSupportedException();
    public bool IsDashboardVisible() => false;
}

internal class FakeVrInput : IOpenVrInput
{
    public EVRInputError SetActionManifestPath(string pchActionManifestPath) => throw new NotSupportedException();
    public EVRInputError GetActionSetHandle(string pchActionSetName, ref ulong pHandle) => throw new NotSupportedException();
    public EVRInputError GetActionHandle(string pchActionName, ref ulong pHandle) => throw new NotSupportedException();
    public EVRInputError GetInputSourceHandle(string pchInputSourcePath, ref ulong pHandle) => throw new NotSupportedException();
    public EVRInputError UpdateActionState(VRActiveActionSet_t[] pSets, uint unSizeOfVRSelectedActionSet_t) => throw new NotSupportedException();
    public virtual EVRInputError GetDigitalActionData(ulong action, ref InputDigitalActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice) => throw new NotSupportedException();
    public EVRInputError GetPoseActionDataRelativeToNow(ulong action, ETrackingUniverseOrigin eOrigin, float fPredictedSecondsFromNow, ref InputPoseActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice) => throw new NotSupportedException();
    public virtual EVRInputError TriggerHapticVibrationAction(ulong action, float fStartSecondsFromNow, float fDurationSeconds, float fFrequency, float fAmplitude, ulong ulRestrictToDevice) => throw new NotSupportedException();
    public EVRInputError GetOriginTrackedDeviceInfo(ulong origin, ref InputOriginInfo_t pOriginInfo, uint unOriginInfoSize) => throw new NotSupportedException();
}
