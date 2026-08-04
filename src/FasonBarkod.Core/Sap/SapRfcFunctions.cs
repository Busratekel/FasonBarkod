namespace FasonBarkod.Core.Sap;



public static class SapRfcFunctions

{

    public static class Sas

    {

        public const string List = "ZMM_N_SAS_L";

        public const string ListLegacy = "ZMM_SAS_L";

        public const string CreateBarcode = "ZMM_N_SAS_B";

        public const string Reprint = "ZMM_N_SAS_B_T";

        /// <summary>GetSAPSASBarcodeSerials — Doqu ASMX (RFC_READ_TABLE kullanılmaz).</summary>
        public const string SerialsSoap = "GetSAPSASBarcodeSerials";

    }

}

