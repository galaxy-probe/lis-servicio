

namespace LisServicioSecure.Models {
    public sealed class WsResponse {
        public bool Ok { get; set; }
        public string Mensaje { get; set; } = "";
        public object? Data { get; set; }
    }

}
