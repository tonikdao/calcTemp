private string _RecalculateRunway(RunwayCalculated runwayCalculated, Runway r, DateTime moment, 
                        List<Notam> notams, 
                        List<WmGroup> wmGroups)
        {
            runwayCalculated.MinHorizontalVisibility = 5000;
            runwayCalculated.MinVerticalVisibility = 1000;
            try
            {
                // ....
                // [CODE CLIPPED -- ANTON KOVALEV]

                //      -----------            ПОГОДА           -----------
                // делим по категориям прогноза 
                var baseWmGroups = wmGroups
                            .Where(
                                w => (w.WmgType == "BASE" || w.WmgType == "FM" || w.WmgType == "BECMG")
                                && w.TimeFrom <= moment && moment <= w.TimeTo);
                var tempoWmGroups = wmGroups
                            .Where(
                                w => w.TimeFrom <= moment && moment <= w.TimeTo)
                            .OrderByDescending(w => w.WmgType); // "TEMPO", "PROB" в голове списка. 
                                                                //Если нет нужного параметра, то подхватится из базового 

                // BASE прогноз, вычисляем актуальные ветра и видимости
                // с учетом направления ВПП
                var wmGroup = baseWmGroups.FirstOrDefault(g => g.WindSpeed != null);
                runwayCalculated.Crosswind = _CalculateWind(wmGroup, r, "WindSpeed", false);
                runwayCalculated.Tailwind = _CalculateWind(wmGroup, r, "WindSpeed", true);
                wmGroup = baseWmGroups.FirstOrDefault(g => g.GustSpeed != null);
                runwayCalculated.CrosswindGust = _CalculateWind(wmGroup, r, "GustSpeed", false);
                runwayCalculated.TailwindGust = _CalculateWind(wmGroup, r, "GustSpeed", true);

                // TEMPO прогноз
                wmGroup = tempoWmGroups.FirstOrDefault(g => g.WindSpeed != null);
                runwayCalculated.CrosswindTempo = _CalculateWind(wmGroup, r, "WindSpeed", false);
                runwayCalculated.TailwindTempo = _CalculateWind(wmGroup, r, "WindSpeed", true);
                wmGroup = tempoWmGroups.FirstOrDefault(g => g.GustSpeed != null);
                runwayCalculated.CrosswindGustTempo = _CalculateWind(wmGroup, r, "GustSpeed", false);
                runwayCalculated.TailwindGustTempo = _CalculateWind(wmGroup, r, "GustSpeed", true);

                // ....
                // [CODE CLIPPED -- ANTON KOVALEV]

                runwayCalculated.StatusReason = "";
                return "Открыта";
            }
            catch (Exception ex)
            {
                _log.ErrorException("Сбой при пересчете ВПП: ", ex);
                runwayCalculated.StatusReason = "";
                return "";
            }
        }



        /// <summary>
        /// Расчет одного из ветров
        /// </summary>
        /// <param name="wmGroup">погодные данные</param>
        /// <param name="rw">ВПП, отсюда берем магнитное склонение (направление) ВПП</param>
        /// <param name="whichSpeed">средний ветер или порывы, одновременно и название свойства в объекте 
        /// группы погодных данных (WmGroup)</param>
        /// <param name="isTailwind">Попутный или боковой ветер</param>
        /// <returns>Скорость ветра в м/с, либо null если не хватает данных</returns>
        private static int? _CalculateWind(WmGroup wmGroup, Runway rw, string whichSpeed, bool isTailwind)
        {
            if (wmGroup == null)
                return null;
            int windSpeed = (int)wmGroup.GetType().GetProperty(whichSpeed).GetValue(wmGroup);
            if (rw.Airport.MagneticShift == null) return null;

            List<int> windDirList = new List<int>();
            int airportMagneticShift = rw.Airport.MagneticShift.Value;
            if (wmGroup.MaxWindDir.HasValue && wmGroup.MinWindDir.HasValue)
            {
                //При указании в погодном сообщении переменного ветра в интервале направлений(конструкция вида, например, 18006MPS 150V220) 
                // боковая составляющая для данной ВПП рассчитывается как худшая(максимальная) для всех возможных направлений в интервале, с шагом 10 градусов
                int minVal = wmGroup.MinWindDir.Value;
                int maxVal = wmGroup.MaxWindDir.Value;
                if (maxVal == minVal)
                {
                    // полностью переменный с низкими значениями - игнорируем
                    return null;
                }
                bool fromMore = (maxVal < minVal); // 350, 360 , 10 , 20 .
                // isBackS - увеличиваем до 360 
                // j - подстраховка - полный круг
                for (int i = minVal, j = 0; (fromMore || i <= maxVal) && j < 36; i += 10, j++)
                {
                    if (i > 360)
                    { // 370->10
                        i -= 360;
                        if (i > maxVal)
                        {
                            break;
                        }
                        fromMore = false;
                    }
                    windDirList.Add(i);
                }
                if (windDirList.Contains(maxVal) == false)
                {
                    windDirList.Add(maxVal);
                }
            }
            else
            {
                if (wmGroup.WindDir.HasValue)
                {
                    windDirList.Add(wmGroup.WindDir.Value);
                }
            }
            if (windDirList.Count == 0) return null;

            if (rw.MagneticCourse.HasValue == false) return null;

            int runwayMagneticHeading = Convert.ToInt32(rw.MagneticCourse.Value);
            //При указании в погодном сообщении переменного ветра в интервале направлений(конструкция вида, например, 18006MPS 150V220) 
            // боковая составляющая для данной ВПП рассчитывается как худшая(максимальная) для всех возможных направлений в интервале, с шагом 10 градусов

            int maxSpeedValue = 0;
            for (int i = 0; i < windDirList.Count; i++)
            {
                int windDir = windDirList[i];
                // [Боковая составляющая ветра] = [Скорость ветра]*ABS(SIN(([Направление ветра]-[Магнитный курс ВПП]-[Магнитное склонение аэропорта])/360*2*3.1416)).

                var currCrosswindDbl = windSpeed * Math.Abs(Math.Sin((windDir - runwayMagneticHeading - airportMagneticShift) / 360.0 * 2.0 * Math.PI));
                if ( isTailwind)
                    currCrosswindDbl = -1*windSpeed * Math.Cos((windDir + 180 - runwayMagneticHeading - airportMagneticShift) / 360.0 * 2.0 * Math.PI);

                int currCrosswind = Convert.ToInt32(currCrosswindDbl);
                if (Math.Abs(maxSpeedValue) < Math.Abs(currCrosswind))
                {
                    maxSpeedValue = currCrosswind;
                }
            }

            return maxSpeedValue;
        }
