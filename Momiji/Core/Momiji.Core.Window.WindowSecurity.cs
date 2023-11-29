using System.Runtime.InteropServices;
using System.Security.AccessControl;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class WindowSecurity : ObjectSecurity<User32.DESKTOP_ACCESS_MASK>
{
    public WindowSecurity()
        : base(
              false,
              ResourceType.WindowObject,
              (SafeHandle?)null,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }
    public WindowSecurity(SafeHandle handle)
        : base(
              false,
              ResourceType.WindowObject,
              handle,
              AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
          )
    {

    }

    public new void Persist(SafeHandle handle)
    {
        Persist(
            handle,
            AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access //| AccessControlSections.Audit
        );
    }
}
