using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Test Alliance Intel Map Reporting Tool")]
[assembly: AssemblyDescription("Scans the EVE client chat logs and"
    + " forwards intel to the Test Alliance Intel Map")]
[assembly: AssemblyCompany(ProductInfo.CompanyName)]
[assembly: AssemblyProduct(ProductInfo.ProductName)]
[assembly: AssemblyCopyright(ProductInfo.CopyrightText)]
[assembly: AssemblyTrademark("")]
// If we change this field, the user's configuration will be lost
[assembly: AssemblyVersion(ProductInfo.ProductVersionPrefix + "0.0")]
[assembly: AssemblyInformationalVersion(ProductInfo.ProductVersion)]
[assembly: AssemblyFileVersion(ProductInfo.ProductVersion)]
[assembly: AssemblyConfiguration(ProductInfo.Configuration)]
[assembly: AssemblyCulture("")]
[assembly: NeutralResourcesLanguage("en-US")]
[assembly: ComVisible(false)]
[assembly: Guid("23c55c96-3331-498f-9419-37bf726a2702")]
