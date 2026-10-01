# Network and key-device enumeration — TL-0111

## Scope

`DeviceEnumerationProvider` collects fixed local read-only evidence as a standard user.
It accepts only cancellation through its public API. There is no network request, device
configuration, media capture, input injection, driver install, elevation, process launch,
permanent service or polling. The provider plugs into the existing inventory runner;
assessment orchestration and guided manual tests belong to later tasks.

## Meaning and ownership

Each key has one semantic owner. A class reports presence only within its stated Windows
source scope. A successful empty enumeration means that source exposed no matching present
device. An empty present-interface list can also reflect a disabled interface, missing driver
or legacy registration; it cannot prove physical absence. Denied, malformed, incomplete,
unsupported or missing enumeration remains unknown.
No class result establishes physical health, connectivity, permission to use a device,
functional success, or readiness for handover.

| Class | Presence key under `devices.` | Meaning |
| --- | --- | --- |
| Wi-Fi | `wifi.present` | Physical adapter reported with wireless LAN/native 802.11 medium |
| Ethernet | `ethernet.present` | Physical adapter reported with 802.3 medium |
| Bluetooth | `bluetooth.radio_interface_present` | Present local Bluetooth radio interface |
| Audio | `audio.render_endpoint_present` | Present render endpoint candidate for a human audio test |
| Microphone | `microphone.capture_endpoint_present` | Present capture endpoint candidate; may be line-in or virtual and does not prove a physical microphone |
| Camera | `camera.video_interface_present` | Present video-camera interface; no scanner inference or frame capture |
| Display | `display.monitor_present` | Present monitor-class device; may be virtual and does not establish a working screen |
| Keyboard | `keyboard.present` | Present keyboard-class device; no key events collected |
| Pointing | `pointing.present` | Present mouse/pointing setup-class device |
| Touch | `touch.session_present` | Current session's reported touch digitizer capability; zero stays unknown |
| USB controller | `usb_controller.host_interface_present` | Present USB host-controller interface; does not establish working ports |

Each class also owns `collection_status`, `problem_count` and `native_error_code`.
The status vocabulary is `collected`, `unavailable`, `access_denied`, `invalid_data`.
Native errors retain only numeric codes; success does not invent an error code. Problem
count records devnodes reporting a configuration problem where that source can expose it.
It is unknown for sources that do not query it. A zero count is not a functional pass.
`devices.functional_test=not_performed` is always explicit for a completed collection.

Duplicate class results become conflicts, independent of input order. Unknown classes and
oversized aggregates are rejected. Class failure retains unrelated successful facts;
aggregate failure priority is invalid data, denied, unavailable. Cleanup failure withholds
all facts and blocks that provider instance.

## Sources and privacy

Network collection queries only `HardwareInterface` and `NdisPhysicalMedium` from local
`root\StandardCimv2:MSFT_NetAdapter`. Virtual adapters are excluded. Unspecified/other or
missing classification cannot establish absence; a complete scan with a definite matching
physical adapter can establish presence. Unknown numeric codes and malformed rows fail
closed. No name, address, identifier, connection profile or network configuration is queried.

Other device classes use compiled SetupAPI class/interface GUIDs and `DIGCF_PRESENT`.
Only numeric devnode status is read. Device handles and devnode references are transient;
device instance IDs, friendly names, symbolic-link paths and raw properties are not requested.
Touch reads only `GetSystemMetrics(SM_DIGITIZER)`; an unavailable input service or ambiguous
zero result does not prove absent hardware. This observation is session-scoped.

Sources return closed enum/boolean/numeric records. Exceptions are reduced to fixed states;
exception text cannot become provider evidence or a log fallback. Normalized observations
remain `WorkshopRestricted` through the existing runner. The approved ordinary-log and
support-export allowlists are unchanged; there is no attachment, report, database, cache,
temporary-file or export write in this provider. Source buffers/handles are released after
collection. Data is observed transiently and only normalized facts leave the adapter.

## Bounds, cancellation and accessibility

The descriptor declares 45 singleton facts plus one possible failure observation, with a
15-second caller timeout and one worker per provider instance. A class enumeration is capped
at 256 devices plus a sentinel; overflow fails the class instead of truncating evidence.
Network WMI enumeration has a three-second deadline, bounded calls and 100 ms waits.
Sources check cancellation between reads and always attempt native cleanup.

Synchronous native calls can outlive cancellation or a caller timeout. The runner stops
waiting and withholds late facts while the provider retains admission until cleanup returns.
A normal cancelled collection can be retried after cleanup. Uncertain cleanup blocks reuse
of that instance and requires investigation and a fresh process. Managed bounds do not bound
Windows' internal allocation or execution time, including the initial SetupAPI list allocation. There is no GPU dependency or background work.

No UI control is added. Fixed class keys, explicit unknown/error states, cancellation and
the unperformed functional test remain available for later accessible presentation. This
task does not claim a keyboard, screen-reader, scaling or human walkthrough that was not run.

## Verification and limitations

Synthetic device trees cover every class, missing/error devices, webcam and touch variants,
malformed/oversized enumeration, numeric-only projection, cleanup, cancellation and retry.
The separate active-machine smoke records only statuses, class booleans, numeric problems
and bounded process measurements. Exact commands, durations and input hashes are committed
under `artifacts/evidence/TL-0111/`.

These tests establish software behavior for represented inputs and one actual Windows
environment. Other webcam/touch devices, manufacturer variants, physical peripheral function,
network access and low-performance hardware remain unverified. No host configuration is
changed to manufacture a fixture state.

Primary contracts reviewed 2026-10-01:

- [MSFT_NetAdapter](https://learn.microsoft.com/en-us/windows/win32/fwp/wmi/netadaptercimprov/msft-netadapter)
- [SetupDiGetClassDevsW](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw)
- [SetupDiEnumDeviceInfo](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdienumdeviceinfo)
- [CM_Get_DevNode_Status](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_devnode_status)
- [GetSystemMetrics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetrics)
- [Enabled device-interface enumeration](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/enumerating-installed-device-interface-classes)
- [Microsoft SDK audio endpoint GUIDs](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.h)
- [Video camera interface](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kscategory-video-camera)
- [Bluetooth radio interface](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-bthport-device-interface)
- [USB host-controller interface](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/guid-devinterface-usb-host-controller)
