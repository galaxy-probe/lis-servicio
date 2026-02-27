using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LisServicioSecure.Models {
    public class Reporte {

        public string Nombre {
            get; set;
        }

        public TipoReporte TipoReporte {
            get; set;
        }

        /// <summary>
        /// Data en base 64
        /// </summary>
        public string Data {
            get; set;
        }
        public string Mensaje {
            get; set;
        }
    }
}
