using System.Collections.Generic;
using System.Globalization;

namespace DwgShadow.Core
{
    public enum UiLanguage { Auto, Russian, English }

    /// <summary>
    /// Localized strings (Russian / English). Call <see cref="Apply"/> before building any UI or message.
    /// </summary>
    public static class L
    {
        public static bool IsRussian { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

        public static void Apply(UiLanguage lang)
        {
            IsRussian = lang == UiLanguage.Russian ||
                        (lang == UiLanguage.Auto && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");
        }

        public static string T(string key)
        {
            if (Strings.TryGetValue(key, out var s)) return IsRussian ? s.Ru : s.En;
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

        static readonly Dictionary<string, (string Ru, string En)> Strings = new Dictionary<string, (string, string)>
        {
            // ---- defaults
            ["def.layer"] = ("Тени", "Shadows"),
            ["def.series"] = ("Тени_серия", "Shadow_series"),
            ["def.zone"] = ("зона", "zone"),

            // ---- windows
            ["win.shadow"] = ("Тени зданий", "Building shadows"),
            ["win.series"] = ("Серия теней", "Shadow series"),
            ["btn.build"] = ("Построить", "Build"),
            ["btn.cancel"] = ("Отмена", "Cancel"),
            ["btn.series"] = ("Серия теней…", "Shadow series…"),
            ["btn.series.tip"] = ("Тени на несколько моментов дня (команда SHDSERIES)", "Shadows for several times of day (SHDSERIES command)"),
            ["lang"] = ("Язык", "Language"),
            ["lang.auto"] = ("Авто", "Auto"),

            // ---- buildings
            ["bld"] = ("Здания", "Buildings"),
            ["src.layer"] = ("Все контуры слоя", "All contours on layer"),
            ["src.select"] = ("Выбрать на чертеже", "Pick in drawing"),
            ["src.select.tip"] = ("Объекты выбираются после нажатия «Построить»", "Objects are picked after you press Build"),
            ["floors"] = ("Этажей", "Floors"),
            ["floorh"] = ("× этаж, м", "× floor, m"),
            ["m"] = ("м", "m"),
            ["fromlabels"] = ("Этажность из подписей внутри контуров", "Floors from labels inside contours"),
            ["fromlabels.tip"] = ("Контуры без подписи получают высоту, указанную выше", "Contours without a label get the height above"),
            ["replace"] = ("Заменить прежние тени этих зданий", "Replace previous shadows of these buildings"),
            ["replace.tip"] = ("Слой целиком — удаляются все объекты на слое теней.\nВыбранные здания — только их тени (и тени, слитые с ними).",
                               "Whole layer — all objects on the shadow layer are removed.\nPicked buildings — only their shadows (and shadows merged with them)."),

            // ---- sun
            ["sun"] = ("Солнце", "Sun"),
            ["sun.manual"] = ("Вручную", "Manual"),
            ["sun.astro"] = ("По дате и времени", "By date and time"),
            ["dir"] = ("Направление, °", "Direction, °"),
            ["dir.tip"] = ("Направление тени: от оси X против часовой стрелки; 90° — вверх", "Shadow direction: from the X axis, counter-clockwise; 90° = up"),
            ["alt"] = ("Высота солнца, °", "Sun altitude, °"),
            ["alt.tip"] = ("45° — длина тени равна высоте здания", "45° — shadow length equals building height"),
            ["date"] = ("Дата", "Date"),
            ["time"] = ("Время", "Time"),
            ["time.tip"] = ("Поясное время ЧЧ:ММ (без летнего времени)", "Local standard time HH:MM (no daylight saving)"),

            // ---- location
            ["exp.location"] = ("Местоположение и защита", "Location and limits"),
            ["city"] = ("Город", "City"),
            ["city.pick"] = ("‹подставить›", "‹choose›"),
            ["lat"] = ("Широта, °", "Latitude, °"),
            ["lat.tip"] = ("Север +, юг −", "North +, south −"),
            ["lon"] = ("Долгота, °", "Longitude, °"),
            ["lon.tip"] = ("Восток +, запад −", "East +, west −"),
            ["utc"] = ("Часовой пояс, UTC", "Time zone, UTC"),
            ["north"] = ("Угол севера, °", "North angle, °"),
            ["north.tip"] = ("Включите, если север на чертеже не вверх: поворот стрелки севера от оси Y, против часовой",
                             "Enable if north is not up: rotation of the north arrow from the Y axis, counter-clockwise"),
            ["minalt"] = ("Не строить при высоте солнца ниже, °", "Skip when sun is lower than, °"),

            // ---- labels / contours
            ["exp.labels"] = ("Подписи и контуры", "Labels and contours"),
            ["label.means"] = ("В подписи", "Label means"),
            ["label.floors"] = ("этажность", "floors"),
            ["label.meters"] = ("высота, м", "height, m"),
            ["label.layer"] = ("Слой подписей", "Label layer"),
            ["anylayer"] = ("‹любой слой›", "‹any layer›"),
            ["label.hint"] = ("Берётся первое число: «9», «9 эт.», «H=27,5». Вложенный контур со своей подписью — отдельная часть здания (башня на стилобате).",
                              "The first number is used: \"9\", \"9 fl\", \"H=27.5\". A nested contour with its own label is a separate part (tower on a podium)."),
            ["courtyards"] = ("Вложенные контуры — дворы", "Nested contours are courtyards"),
            ["contours.hint"] = ("Полилинии (в том числе с дугами) и окружности в пространстве модели.",
                                 "Polylines (arcs included) and circles in model space."),

            // ---- appearance
            ["exp.style"] = ("Оформление", "Appearance"),
            ["outlayer"] = ("Слой теней", "Shadow layer"),
            ["color"] = ("Цвет", "Color"),
            ["units"] = ("Единицы чертежа", "Drawing units"),
            ["units.m"] = ("метры", "meters"),
            ["units.cm"] = ("сантиметры", "centimeters"),
            ["units.mm"] = ("миллиметры", "millimeters"),
            ["contour"] = ("Контур", "Outline"),
            ["hatch"] = ("Штриховка", "Hatch"),
            ["scale"] = ("масштаб", "scale"),
            ["transp"] = ("прозр., %", "transp., %"),
            ["subtract"] = ("Вырезать здания из тени", "Cut buildings out of shadows"),
            ["toback"] = ("Штриховку на задний план", "Send hatch to back"),
            ["color.bylayer"] = ("ПоСлою", "ByLayer"),
            ["color.byblock"] = ("ПоБлоку", "ByBlock"),
            ["color.n"] = ("Цвет {0}", "Color {0}"),
            ["color.err"] = ("Диалог выбора цвета недоступен: {0}", "Color dialog is not available: {0}"),

            // ---- series
            ["interval"] = ("Интервал", "Time range"),
            ["from"] = ("с", "from"),
            ["to"] = ("до", "to"),
            ["step"] = ("шаг, мин", "step, min"),
            ["s.prefix"] = ("Слой (префикс)", "Layer prefix"),
            ["s.perlayer"] = ("Отдельный слой на каждый момент", "Separate layer for each time"),
            ["s.labels"] = ("Подписи времени, высота, м", "Time labels, height, m"),
            ["s.zone"] = ("Общая зона затенения", "Combined shadow zone"),
            ["s.zonehatch"] = ("со штриховкой", "hatched"),
            ["s.note"] = ("Наглядная серия для оформления, а не нормативный расчёт инсоляции.",
                          "An illustrative series for drawings, not a regulatory insolation analysis."),
            ["col.time"] = ("Время", "Time"),
            ["col.az"] = ("Азимут, °", "Azimuth, °"),
            ["col.alt"] = ("Высота, °", "Altitude, °"),
            ["col.ratio"] = ("Тень/H", "Shadow/H"),
            ["st.below"] = ("под горизонтом", "below horizon"),
            ["st.low"] = ("низко — пропуск", "too low — skipped"),

            // ---- preview
            ["pv.building"] = ("Условное здание, H = {0:0.#} м", "Sample building, H = {0:0.#} m"),
            ["pv.belowh"] = ("Солнце под горизонтом ({0:0.0}°)", "Sun below horizon ({0:0.0}°)"),
            ["pv.low"] = ("Солнце ниже {0:0.#}° ({1:0.0}°) — тень не строится", "Sun below {0:0.#}° ({1:0.0}°) — no shadow"),
            ["pv.az"] = ("азимут {0:0.0}° · ", "azimuth {0:0.0}° · "),
            ["pv.info"] = ("высота {0:0.0}° · тень {1:0.00}×H = {2:0.#} м", "altitude {0:0.0}° · shadow {1:0.00}×H = {2:0.#} m"),
            ["pv.short"] = (" (укорочено)", " (shortened)"),
            ["pv.section"] = ("Разрез", "Section"),
            ["pv.north"] = ("С", "N"),
            ["pv.moments"] = ("Моментов: {0} из {1}", "Times: {0} of {1}"),
            ["pv.none"] = ("Во всём интервале солнце ниже {0:0.#}°", "Sun is below {0:0.#}° for the whole range"),
            ["pv.error"] = ("Превью недоступно: {0}", "Preview unavailable: {0}"),

            // ---- validation
            ["v.fields"] = ("Исправьте поля, выделенные красным.", "Fix the fields highlighted in red."),
            ["v.number"] = ("Введите число", "Enter a number"),
            ["v.integer"] = ("Введите целое число", "Enter a whole number"),
            ["v.layer"] = ("Не выбран слой с контурами зданий.", "Building layer is not selected."),
            ["v.floors"] = ("Этажность должна быть от 1 до 300.", "Floors must be between 1 and 300."),
            ["v.floorh"] = ("Высота этажа должна быть от 0 до 20 м.", "Floor height must be between 0 and 20 m."),
            ["v.minalt"] = ("Минимальная высота солнца — от 0,5° до 60°.", "Minimum sun altitude must be 0.5°–60°."),
            ["v.lat"] = ("Широта должна быть от −90 до 90°.", "Latitude must be between −90 and 90°."),
            ["v.lon"] = ("Долгота должна быть от −180 до 180°.", "Longitude must be between −180 and 180°."),
            ["v.utc"] = ("Часовой пояс должен быть от −12 до +14.", "Time zone must be between −12 and +14."),
            ["v.alt"] = ("Высота солнца должна быть от 0 до 90°.", "Sun altitude must be between 0 and 90°."),
            ["v.time"] = ("Время указано неверно (нужно ЧЧ:ММ).", "Invalid time (use HH:MM)."),
            ["v.start"] = ("Неверное время начала.", "Invalid start time."),
            ["v.end"] = ("Неверное время окончания.", "Invalid end time."),
            ["v.order"] = ("Время окончания должно быть позже начала.", "End time must be after start time."),
            ["v.step"] = ("Шаг — от 5 до 720 минут.", "Step must be 5–720 minutes."),
            ["v.many"] = ("Слишком много шагов (больше 200).", "Too many steps (over 200)."),
            ["v.prefix"] = ("Не задано имя слоя серии.", "Series layer name is empty."),
            ["v.labelh"] = ("Высота подписи должна быть больше 0.", "Label height must be greater than 0."),
            ["v.outlayer"] = ("Не задан слой для теней.", "Shadow layer is empty."),
            ["v.nothing"] = ("Включите контур или штриховку — иначе нечего рисовать.", "Enable outline or hatch — otherwise nothing is drawn."),
            ["v.transp"] = ("Прозрачность штриховки — от 0 до 90 %.", "Hatch transparency must be 0–90 %."),
            ["v.scale"] = ("Масштаб штриховки должен быть больше 0.", "Hatch scale must be greater than 0."),
            ["v.sunbelow"] = ("В выбранное время солнце под горизонтом ({0:0.0}°). Выберите другое время.",
                              "The sun is below the horizon at this time ({0:0.0}°). Choose another time."),
            ["v.sunlow"] = ("Солнце ниже {0:0.#}° ({1:0.0}°): тени получатся слишком длинными. Выберите другое время или уменьшите порог.",
                            "The sun is below {0:0.#}° ({1:0.0}°): shadows would be too long. Choose another time or lower the limit."),
            ["v.seriesnone"] = ("Во всём интервале солнце ниже {0:0.#}°. Измените дату или интервал.",
                                "The sun is below {0:0.#}° for the whole range. Change the date or time range."),

            // ---- command line
            ["msg.loaded"] = ("DwgShadow загружен. Команды: SHD — тени, SHDSERIES — серия теней (-SHD, -SHDSERIES — без окна).",
                              "DwgShadow loaded. Commands: SHD — shadows, SHDSERIES — shadow series (-SHD, -SHDSERIES — no dialog)."),
            ["msg.prefix"] = ("Тени: ", "Shadows: "),
            ["msg.error"] = ("ошибка — {0}\nПодробности в журнале: {1}", "error — {0}\nDetails in the log: {1}"),
            ["msg.belowh"] = ("солнце под горизонтом ({0:0.0}°). Построение отменено.", "the sun is below the horizon ({0:0.0}°). Cancelled."),
            ["msg.low"] = ("солнце слишком низко ({0:0.0}° < {1:0.#}°), тени были бы слишком длинными. Построение отменено.",
                           "the sun is too low ({0:0.0}° < {1:0.#}°), shadows would be too long. Cancelled."),
            ["msg.sunaz"] = ("азимут солнца {0:0.0}°, ", "sun azimuth {0:0.0}°, "),
            ["msg.dir"] = ("высота солнца {0:0.0}°, направление тени {1:0.0}°, длина тени = {2:0.00} × высота.",
                           "sun altitude {0:0.0}°, shadow direction {1:0.0}°, shadow length = {2:0.00} × height."),
            ["msg.done"] = ("зданий {0}, контуров тени {1}, слой «{2}».", "{0} buildings, {1} shadow contours, layer \"{2}\"."),
            ["msg.nolayer"] = ("на слое «{0}» нет полилиний и окружностей в пространстве модели.", "no polylines or circles on layer \"{0}\" in model space."),
            ["msg.noselect"] = ("ничего не выбрано.", "nothing selected."),
            ["msg.noclosed"] = ("среди выбранных объектов нет замкнутых контуров.", "no closed contours among the selected objects."),
            ["msg.select"] = ("\nВыберите замкнутые контуры зданий: ", "\nSelect closed building contours: "),
            ["msg.nested"] = ("добавлено вложенных контуров (дворы, части зданий): {0}.", "nested contours added (courtyards, building parts): {0}."),
            ["msg.linked"] = ("добавлено зданий с общими или пересекающимися тенями: {0}.", "buildings with shared or overlapping shadows added: {0}."),
            ["msg.kept"] = ("из них с прежней высотой и солнцем (их не выбирали): {0}.", "of them kept their previous height and sun (not picked): {0}."),
            ["msg.removed"] = ("удалено прежних объектов: {0}.", "previous objects removed: {0}."),
            ["msg.skipped"] = ("пропущено незамкнутых или неподдерживаемых объектов: {0}.", "open or unsupported objects skipped: {0}."),
            ["msg.default"] = ("у {0} зданий нет подписи — взята высота по умолчанию {1:0.##} м.", "{0} buildings have no label — default height {1:0.##} m used."),
            ["msg.failed"] = ("не удалось обработать контуров: {0} (пропущены).", "failed to process contours: {0} (skipped)."),
            ["msg.hatchfail"] = ("штриховка не построилась для {0} контуров (контуры нарисованы).", "hatch failed for {0} contours (outlines are drawn)."),
            ["msg.locked"] = ("Слой «{0}» заблокирован. Разблокируйте его и повторите.", "Layer \"{0}\" is locked. Unlock it and try again."),
            ["msg.frozen"] = ("Слой «{0}» заморожен. Разморозьте его и повторите.", "Layer \"{0}\" is frozen. Thaw it and try again."),
            ["msg.s.none"] = ("во всём интервале солнце ниже {0:0.#}°. Построение отменено.", "the sun is below {0:0.#}° for the whole range. Cancelled."),
            ["msg.s.done"] = ("серия {0:dd.MM.yyyy}: моментов {1} ({2}–{3}), контуров {4}.", "series {0:yyyy-MM-dd}: {1} times ({2}–{3}), {4} contours."),
            ["msg.s.low"] = ("пропущены моменты с низким солнцем (< {0:0.#}°): {1}.", "times with low sun skipped (< {0:0.#}°): {1}."),
        };
    }
}
