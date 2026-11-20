using UnityEngine;

namespace Ashline.City
{
    public enum FacadeStyle { Brick, Plaster, Office, Curtain, Warehouse }

    public enum GroundFloor { None, Shops, Lobby, Loading, Cafe }

    public enum RoofKind { Flat, Hip }

    public class Building
    {
        public Rect footprint;          // local plan rect (x, z), centered on the lot origin
        public FacadeStyle style;
        public GroundFloor ground;
        public RoofKind roof;
        public int floors;
        public float floorHeight;
        public float groundHeight;
        public string wall;             // CityArt surface id
        public string trim;
        public int seed;
        /// <summary>Upper part pulled back from the street on towers (0 = none).</summary>
        public float setback;
        public int setbackFloor;
        /// <summary>Hotel style balcony slabs with parapets on the long faces.</summary>
        public bool balconies;
        /// <summary>Index into CityArt.awnings for shop canopies (-1 = none).</summary>
        public int awning = -1;

        public float Height => groundHeight + (floors - 1) * floorHeight;
    }
}
