// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.
//
// To add a suppression to this file, right-click the message in the 
// Code Analysis results, point to "Suppress Message", and click 
// "In Suppression File".
// You do not need to add suppressions to this file manually.

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Globalization",
    "CA1308:NormalizeStringsToUppercase",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelSession.#HashPassword(System.String)",
    Justification = "Normalizing to Lowercase because that's what the server requires")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Usage",
    "CA2213:DisposableFieldsShouldBeDisposed",
    MessageId = "session",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelReporter.#Dispose(System.Boolean)",
    Justification = "session will disposed by the call to Stop()")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1822:MarkMembersAsStatic",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelReporter.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1811:AvoidUncalledPrivateCode",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelReporter.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1811:AvoidUncalledPrivateCode",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelEventArgs.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1822:MarkMembersAsStatic",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelSession.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1822:MarkMembersAsStatic",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelEventArgs.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1822:MarkMembersAsStatic",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelChannel.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Microsoft.Performance",
    "CA1811:AvoidUncalledPrivateCode",
    Scope = "member",
    Target = "PleaseIgnore.IntelMap.IntelChannel.#ObjectInvariant()",
    Justification = "Invariant method for CodeContracts")]
