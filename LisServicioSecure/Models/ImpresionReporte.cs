
namespace LisServicioSecure.Models {
    public class ImpresionReporte {
        /// <summary>
        /// mensaje para mostrar en pantalla
        /// </summary>
        public string Mensaje// the Name property
        {
            get; set;
        }

        private readonly List<Reporte> reportes;

        public ImpresionReporte() {
            reportes = new List<Reporte>();
        }

        public List<Reporte> Reportes {
            get {
                return reportes;
            }
        }

        public void AddReporte(Reporte reporte) {
            reportes.Add(reporte);
        }

    }
}
