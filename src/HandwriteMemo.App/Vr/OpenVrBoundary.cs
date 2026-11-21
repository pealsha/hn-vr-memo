using System.Text;
using Valve.VR;

namespace HandwriteMemo.App;

internal interface IOpenVrApplications
{
    EVRApplicationError AddApplicationManifest(string pchApplicationManifestFullPath, bool bTemporary);
    EVRApplicationError RemoveApplicationManifest(string pchApplicationManifestFullPath);
    bool IsApplicationInstalled(string pchAppKey);
    EVRApplicationError IdentifyApplication(uint unProcessId, string pchAppKey);
    uint GetApplicationPropertyString(string pchAppKey, EVRApplicationProperty eProperty, StringBuilder pchPropertyValueBuffer, uint unPropertyValueBufferLen, ref EVRApplicationError peError);
    EVRApplicationError SetApplicationAutoLaunch(string pchAppKey, bool bAutoLaunch);
    bool GetApplicationAutoLaunch(string pchAppKey);
}

internal sealed class OpenVrApplicationsAdapter(CVRApplications api) : IOpenVrApplications
{
    public EVRApplicationError AddApplicationManifest(string pchApplicationManifestFullPath, bool bTemporary) =>
        api.AddApplicationManifest(pchApplicationManifestFullPath, bTemporary);
    public EVRApplicationError RemoveApplicationManifest(string pchApplicationManifestFullPath) =>
        api.RemoveApplicationManifest(pchApplicationManifestFullPath);
    public bool IsApplicationInstalled(string pchAppKey) =>
        api.IsApplicationInstalled(pchAppKey);
    public EVRApplicationError IdentifyApplication(uint unProcessId, string pchAppKey) =>
        api.IdentifyApplication(unProcessId, pchAppKey);
    public uint GetApplicationPropertyString(string pchAppKey, EVRApplicationProperty eProperty, StringBuilder pchPropertyValueBuffer, uint unPropertyValueBufferLen, ref EVRApplicationError peError) =>
        api.GetApplicationPropertyString(pchAppKey, eProperty, pchPropertyValueBuffer, unPropertyValueBufferLen, ref peError);
    public EVRApplicationError SetApplicationAutoLaunch(string pchAppKey, bool bAutoLaunch) =>
        api.SetApplicationAutoLaunch(pchAppKey, bAutoLaunch);
    public bool GetApplicationAutoLaunch(string pchAppKey) =>
        api.GetApplicationAutoLaunch(pchAppKey);
}

internal interface IOpenVrSystem
{
    bool GetTimeSinceLastVsync(ref float pfSecondsSinceLastVsync, ref ulong pulFrameCounter);
    void GetDXGIOutputInfo(ref int pnAdapterIndex);
    void GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin eOrigin, float fPredictedSecondsToPhotonsFromNow, TrackedDevicePose_t[] pTrackedDevicePoseArray);
    uint GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole unDeviceType);
    float GetFloatTrackedDeviceProperty(uint unDeviceIndex, ETrackedDeviceProperty prop, ref ETrackedPropertyError pError);
    bool PollNextEvent(ref VREvent_t pEvent, uint uncbVREvent);
}

internal sealed class OpenVrSystemAdapter(CVRSystem api) : IOpenVrSystem
{
    public bool GetTimeSinceLastVsync(ref float pfSecondsSinceLastVsync, ref ulong pulFrameCounter) =>
        api.GetTimeSinceLastVsync(ref pfSecondsSinceLastVsync, ref pulFrameCounter);
    public void GetDXGIOutputInfo(ref int pnAdapterIndex) =>
        api.GetDXGIOutputInfo(ref pnAdapterIndex);
    public void GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin eOrigin, float fPredictedSecondsToPhotonsFromNow, TrackedDevicePose_t[] pTrackedDevicePoseArray) =>
        api.GetDeviceToAbsoluteTrackingPose(eOrigin, fPredictedSecondsToPhotonsFromNow, pTrackedDevicePoseArray);
    public uint GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole unDeviceType) =>
        api.GetTrackedDeviceIndexForControllerRole(unDeviceType);
    public float GetFloatTrackedDeviceProperty(uint unDeviceIndex, ETrackedDeviceProperty prop, ref ETrackedPropertyError pError) =>
        api.GetFloatTrackedDeviceProperty(unDeviceIndex, prop, ref pError);
    public bool PollNextEvent(ref VREvent_t pEvent, uint uncbVREvent) =>
        api.PollNextEvent(ref pEvent, uncbVREvent);
}

internal interface IOpenVrOverlay
{
    EVROverlayError CreateOverlay(string pchOverlayKey, string pchOverlayName, ref ulong pOverlayHandle);
    EVROverlayError DestroyOverlay(ulong ulOverlayHandle);
    EVROverlayError SetOverlayFlag(ulong ulOverlayHandle, VROverlayFlags eOverlayFlag, bool bEnabled);
    EVROverlayError SetOverlayColor(ulong ulOverlayHandle, float fRed, float fGreen, float fBlue);
    EVROverlayError SetOverlaySortOrder(ulong ulOverlayHandle, uint unSortOrder);
    EVROverlayError SetOverlayWidthInMeters(ulong ulOverlayHandle, float fWidthInMeters);
    EVROverlayError SetOverlayTransformAbsolute(ulong ulOverlayHandle, ETrackingUniverseOrigin eTrackingOrigin, ref HmdMatrix34_t pmatTrackingOriginToOverlayTransform);
    EVROverlayError SetOverlayTransformTrackedDeviceRelative(ulong ulOverlayHandle, uint unTrackedDevice, ref HmdMatrix34_t pmatTrackedDeviceToOverlayTransform);
    EVROverlayError ShowOverlay(ulong ulOverlayHandle);
    EVROverlayError HideOverlay(ulong ulOverlayHandle);
    EVROverlayError WaitFrameSync(uint nTimeoutMs);
    bool PollNextOverlayEvent(ulong ulOverlayHandle, ref VREvent_t pEvent, uint uncbVREvent);
    EVROverlayError SetOverlayInputMethod(ulong ulOverlayHandle, VROverlayInputMethod eInputMethod);
    EVROverlayError SetOverlayMouseScale(ulong ulOverlayHandle, ref HmdVector2_t pvecMouseScale);
    EVROverlayError SetOverlayTexture(ulong ulOverlayHandle, ref Texture_t pTexture);
    EVROverlayError ClearOverlayTexture(ulong ulOverlayHandle);
    EVROverlayError CreateDashboardOverlay(string pchOverlayKey, string pchOverlayFriendlyName, ref ulong pMainHandle, ref ulong pThumbnailHandle);
    bool IsDashboardVisible();
}

internal sealed class OpenVrOverlayAdapter(CVROverlay api) : IOpenVrOverlay
{
    public EVROverlayError CreateOverlay(string pchOverlayKey, string pchOverlayName, ref ulong pOverlayHandle) =>
        api.CreateOverlay(pchOverlayKey, pchOverlayName, ref pOverlayHandle);
    public EVROverlayError DestroyOverlay(ulong ulOverlayHandle) =>
        api.DestroyOverlay(ulOverlayHandle);
    public EVROverlayError SetOverlayFlag(ulong ulOverlayHandle, VROverlayFlags eOverlayFlag, bool bEnabled) =>
        api.SetOverlayFlag(ulOverlayHandle, eOverlayFlag, bEnabled);
    public EVROverlayError SetOverlayColor(ulong ulOverlayHandle, float fRed, float fGreen, float fBlue) =>
        api.SetOverlayColor(ulOverlayHandle, fRed, fGreen, fBlue);
    public EVROverlayError SetOverlaySortOrder(ulong ulOverlayHandle, uint unSortOrder) =>
        api.SetOverlaySortOrder(ulOverlayHandle, unSortOrder);
    public EVROverlayError SetOverlayWidthInMeters(ulong ulOverlayHandle, float fWidthInMeters) =>
        api.SetOverlayWidthInMeters(ulOverlayHandle, fWidthInMeters);
    public EVROverlayError SetOverlayTransformAbsolute(ulong ulOverlayHandle, ETrackingUniverseOrigin eTrackingOrigin, ref HmdMatrix34_t pmatTrackingOriginToOverlayTransform) =>
        api.SetOverlayTransformAbsolute(ulOverlayHandle, eTrackingOrigin, ref pmatTrackingOriginToOverlayTransform);
    public EVROverlayError SetOverlayTransformTrackedDeviceRelative(ulong ulOverlayHandle, uint unTrackedDevice, ref HmdMatrix34_t pmatTrackedDeviceToOverlayTransform) =>
        api.SetOverlayTransformTrackedDeviceRelative(ulOverlayHandle, unTrackedDevice, ref pmatTrackedDeviceToOverlayTransform);
    public EVROverlayError ShowOverlay(ulong ulOverlayHandle) =>
        api.ShowOverlay(ulOverlayHandle);
    public EVROverlayError HideOverlay(ulong ulOverlayHandle) =>
        api.HideOverlay(ulOverlayHandle);
    public EVROverlayError WaitFrameSync(uint nTimeoutMs) =>
        api.WaitFrameSync(nTimeoutMs);
    public bool PollNextOverlayEvent(ulong ulOverlayHandle, ref VREvent_t pEvent, uint uncbVREvent) =>
        api.PollNextOverlayEvent(ulOverlayHandle, ref pEvent, uncbVREvent);
    public EVROverlayError SetOverlayInputMethod(ulong ulOverlayHandle, VROverlayInputMethod eInputMethod) =>
        api.SetOverlayInputMethod(ulOverlayHandle, eInputMethod);
    public EVROverlayError SetOverlayMouseScale(ulong ulOverlayHandle, ref HmdVector2_t pvecMouseScale) =>
        api.SetOverlayMouseScale(ulOverlayHandle, ref pvecMouseScale);
    public EVROverlayError SetOverlayTexture(ulong ulOverlayHandle, ref Texture_t pTexture) =>
        api.SetOverlayTexture(ulOverlayHandle, ref pTexture);
    public EVROverlayError ClearOverlayTexture(ulong ulOverlayHandle) =>
        api.ClearOverlayTexture(ulOverlayHandle);
    public EVROverlayError CreateDashboardOverlay(string pchOverlayKey, string pchOverlayFriendlyName, ref ulong pMainHandle, ref ulong pThumbnailHandle) =>
        api.CreateDashboardOverlay(pchOverlayKey, pchOverlayFriendlyName, ref pMainHandle, ref pThumbnailHandle);
    public bool IsDashboardVisible() =>
        api.IsDashboardVisible();
}

internal interface IOpenVrInput
{
    EVRInputError SetActionManifestPath(string pchActionManifestPath);
    EVRInputError GetActionSetHandle(string pchActionSetName, ref ulong pHandle);
    EVRInputError GetActionHandle(string pchActionName, ref ulong pHandle);
    EVRInputError GetInputSourceHandle(string pchInputSourcePath, ref ulong pHandle);
    EVRInputError UpdateActionState(VRActiveActionSet_t[] pSets, uint unSizeOfVRSelectedActionSet_t);
    EVRInputError GetDigitalActionData(ulong action, ref InputDigitalActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice);
    EVRInputError GetPoseActionDataRelativeToNow(ulong action, ETrackingUniverseOrigin eOrigin, float fPredictedSecondsFromNow, ref InputPoseActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice);
    EVRInputError TriggerHapticVibrationAction(ulong action, float fStartSecondsFromNow, float fDurationSeconds, float fFrequency, float fAmplitude, ulong ulRestrictToDevice);
    EVRInputError GetOriginTrackedDeviceInfo(ulong origin, ref InputOriginInfo_t pOriginInfo, uint unOriginInfoSize);
}

internal sealed class OpenVrInputAdapter(CVRInput api) : IOpenVrInput
{
    public EVRInputError SetActionManifestPath(string pchActionManifestPath) =>
        api.SetActionManifestPath(pchActionManifestPath);
    public EVRInputError GetActionSetHandle(string pchActionSetName, ref ulong pHandle) =>
        api.GetActionSetHandle(pchActionSetName, ref pHandle);
    public EVRInputError GetActionHandle(string pchActionName, ref ulong pHandle) =>
        api.GetActionHandle(pchActionName, ref pHandle);
    public EVRInputError GetInputSourceHandle(string pchInputSourcePath, ref ulong pHandle) =>
        api.GetInputSourceHandle(pchInputSourcePath, ref pHandle);
    public EVRInputError UpdateActionState(VRActiveActionSet_t[] pSets, uint unSizeOfVRSelectedActionSet_t) =>
        api.UpdateActionState(pSets, unSizeOfVRSelectedActionSet_t);
    public EVRInputError GetDigitalActionData(ulong action, ref InputDigitalActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice) =>
        api.GetDigitalActionData(action, ref pActionData, unActionDataSize, ulRestrictToDevice);
    public EVRInputError GetPoseActionDataRelativeToNow(ulong action, ETrackingUniverseOrigin eOrigin, float fPredictedSecondsFromNow, ref InputPoseActionData_t pActionData, uint unActionDataSize, ulong ulRestrictToDevice) =>
        api.GetPoseActionDataRelativeToNow(action, eOrigin, fPredictedSecondsFromNow, ref pActionData, unActionDataSize, ulRestrictToDevice);
    public EVRInputError TriggerHapticVibrationAction(ulong action, float fStartSecondsFromNow, float fDurationSeconds, float fFrequency, float fAmplitude, ulong ulRestrictToDevice) =>
        api.TriggerHapticVibrationAction(action, fStartSecondsFromNow, fDurationSeconds, fFrequency, fAmplitude, ulRestrictToDevice);
    public EVRInputError GetOriginTrackedDeviceInfo(ulong origin, ref InputOriginInfo_t pOriginInfo, uint unOriginInfoSize) =>
        api.GetOriginTrackedDeviceInfo(origin, ref pOriginInfo, unOriginInfoSize);
}
