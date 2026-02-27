using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LisServicioSecure.Models {
    public sealed class WsRequest {
        public string Accion { get; set; } = "";     // getmac | print_zpl | print_pdf
        //public string? Tipo { get; set; }            // normal | zebra (opcional)
        //public string? Data { get; set; }            // base64
        //public string? JobId { get; set; }           // opcional
        public List<SignedTicket>? Tickets { get; set; }  // 👈 agrega esto
    }
}
