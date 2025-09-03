# Arma3.DllExport - [Download](https://www.nuget.org/packages/Arma3.DllExport/)
Simplify C# extensions for ARMA

```PM> Install-Package Arma3.DllExport```

[![Demo](https://img.youtube.com/vi/MXRBckxwqEw/0.jpg)](http://www.youtube.com/watch?v=MXRBckxwqEw)

```csharp
public class SomeClass
{
    [ArmaDllExport]
    public static string Invoke(string input, int size)
    {
        return input;
    }
}
```
