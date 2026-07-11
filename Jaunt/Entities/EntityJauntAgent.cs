using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.API.Server;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices.Marshalling;
using Vintagestory.API;
using System.Threading.Tasks;
using System.Text;
using System.Globalization;
using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;


#nullable disable

namespace Jaunt.Entities
{

    public class EntityJauntAgent : EntityAgent
    {
        /*
        public override double GetWalkSpeedMultiplier(double groundDragFactor = 0.3)
        {
            int y1 = (int)(Pos.InternalY - 0.05f);
            int y2 = (int)(Pos.InternalY + 0.01f);

            Block belowBlock = World.BlockAccessor.GetBlockRaw((int)Pos.X, y1, (int)Pos.Z);

            insidePos.Set((int)Pos.X, y2, (int)Pos.Z);
            insidePos.SetDimension(Pos.Dimension);
            insideBlock = World.BlockAccessor.GetBlock(insidePos);

            var ebc = GetBehavior<Behaviors.EntityBehaviorJauntWalkspeed>();
            double multiplier = (servercontrols.Sneak ? GlobalConstants.SneakSpeedMultiplier : 1.0) * (servercontrols.Sprint ? GlobalConstants.SprintSpeedMultiplier : 1.0);

            if (FeetInLiquid){multiplier /= 2.5;}
            multiplier *= belowBlock.WalkSpeedMultiplier * ebc?.ExtraSpeedModifier ?? 1.0 * (y1 == y2 ? 1 : insideBlock.WalkSpeedMultiplier);

            return multiplier;
        }
        */
    }
}