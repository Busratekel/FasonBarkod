using System.Reflection;
using SapNwRfc;

namespace FasonBarkod.Infrastructure.Sap;

public static class SapRfcDiagnostics
{
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static string DescribeFunction(SapConnection connection, string functionName)
    {
        try
        {
            var metadata = connection.GetFunctionMetadata(functionName);
            return string.Join(
                ", ",
                metadata.Parameters.Select(p => $"{p.Name}({p.Type})"));
        }
        catch (Exception ex)
        {
            return $"metadata alınamadı: {ex.Message}";
        }
    }

    public static string DescribeTypes(SapConnection connection, params string[] typeNames)
    {
        var parts = new List<string>();

        foreach (var typeName in typeNames)
        {
            try
            {
                var metadata = connection.GetTypeMetadata(typeName);
                parts.Add(
                    $"{typeName}=[{string.Join(", ", metadata.Fields.Select(f => f.Name))}]");
            }
            catch
            {
                parts.Add($"{typeName}=(yok)");
            }
        }

        return string.Join("; ", parts);
    }

    /// <summary>
    /// RFC parametresinin gerçek satır yapısını (alan adları) döndürür — global DDIC tip adı bilinmese de
    /// çalışır. IT_HATA gibi anonim/yerel yapılar için tip adı tahmin etmeye gerek kalmaz.
    /// </summary>
    public static string DescribeTableParameterStructure(SapConnection connection, string functionName, string parameterName)
    {
        try
        {
            var metadata = connection.GetFunctionMetadata(functionName);
            if (!metadata.Parameters.TryGetValue(parameterName, out var parameter))
            {
                return $"{parameterName}=(parametre bulunamadı)";
            }

            var parameterType = parameter.GetType();
            var interopField = parameterType.GetField("_interop", AnyInstance);
            var descriptionField = parameterType.GetField("_parameterDescription", AnyInstance);

            if (interopField is null || descriptionField is null)
            {
                return $"{parameterName}=(reflection alanları bulunamadı — SapNwRfc sürümü değişmiş olabilir)";
            }

            var interop = interopField.GetValue(parameter);
            var description = descriptionField.GetValue(parameter);
            var handleField = description?.GetType().GetField("TypeDescHandle", AnyInstance);

            if (handleField is null)
            {
                return $"{parameterName}=(TypeDescHandle alanı bulunamadı)";
            }

            var handle = (IntPtr)handleField.GetValue(description)!;
            if (handle == IntPtr.Zero)
            {
                return $"{parameterName}=(yapı yok — basit alan)";
            }

            var typeMetadataType = typeof(SapConnection).Assembly.GetType("SapNwRfc.SapTypeMetadata")
                ?? throw new InvalidOperationException("SapTypeMetadata tipi bulunamadı.");

            var typeMetadata = Activator.CreateInstance(
                typeMetadataType,
                AnyInstance,
                binder: null,
                args: [interop!, handle],
                culture: null) as ISapTypeMetadata;

            if (typeMetadata is null)
            {
                return $"{parameterName}=(SapTypeMetadata oluşturulamadı)";
            }

            return $"{parameterName}=[{string.Join(", ", typeMetadata.Fields.Select(f => $"{f.Name}({f.Type})"))}]";
        }
        catch (Exception ex)
        {
            return $"{parameterName}=(okunamadı: {ex.Message})";
        }
    }
}
