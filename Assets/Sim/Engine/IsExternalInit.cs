// Compiler shim. `init` accessors and positional records emit a reference to
// System.Runtime.CompilerServices.IsExternalInit, which netstandard2.1 does not define.
// Declaring it here is the standard workaround; it is internal, so each assembly that
// needs records carries its own copy without colliding.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
