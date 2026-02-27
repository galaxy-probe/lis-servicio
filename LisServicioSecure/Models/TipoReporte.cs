namespace LisServicioSecure.Models {
    public class TipoReporte {
        public enum TipoFormato {
            Pdf,
            Zpl //para etiquetas en impresoras zebra
        }
        public TipoFormato Formato {
            get; set;
        }
    }
}
