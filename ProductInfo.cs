// Stores common constants used in individual AssemblyInfo.cs
internal static class ProductInfo {
    // Content for AssemblyCompanyAttribute
    internal const string CompanyName = "Test Alliance Please Ignore";

    // Content for AssemblyProductAttribute
    internal const string ProductName = "Test Alliance Client Tools";

    // Content for AssemblyCopyrightAttribute
    internal const string CopyrightText = "Copyright © 2013";

    // Prefix for AssemblyVersion*Attribute
    internal const string ProductVersionPrefix = "0.5.";

    // Content for AssemblyVersion*Attribute
    internal const string ProductVersion = ProductVersionPrefix + "2.133";

    // Content for AssemblyConfigurationAttribute
#if DEBUG
    internal const string Configuration = "Debug";
#else
    internal const string Configuration = "Release";
#endif
}
