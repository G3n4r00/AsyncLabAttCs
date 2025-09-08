using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AsyncLab
{
    public class DiferencaMunicipio
    {
        public Municipio? MunicipioAntigo { get; set; }
        public Municipio Municipio { get; set; }
        public string TipoMudanca { get; set; } // "ADICIONADO", "REMOVIDO", "ALTERADO"
    }
}

