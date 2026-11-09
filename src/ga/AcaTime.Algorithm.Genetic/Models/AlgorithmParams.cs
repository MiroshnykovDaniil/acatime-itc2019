using System.Globalization;

namespace AcaTime.Algorithm.Genetic.Models
{
    /// <summary>
    /// Параметри алгоритму
    /// </summary>
    public class AlgorithmParams
    {
        /// <summary>
        /// Кількість результатів, які потрібно знайти
        /// </summary>
        public int ResultsCount { get; set; }

        /// <summary>
        /// Максимальна кількість ітерацій
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Максимальний час роботи алгоритму в секундах
        /// </summary>
        public int TimeoutInSeconds { get; set; }

        /// <summary>
        /// Кількість кращих слотів для вибору
        /// </summary>
        public int SlotsTopK { get; set; }

        /// <summary>
        /// Кількість кращих доменів для вибору
        /// </summary>
        public int DomainsTopK { get; set; }


        /// <summary>
        /// Температура для вибору слотів
        /// </summary>
        public double SlotsTemperature { get; set; }


        /// <summary>
        /// Температура для вибору доменів
        /// </summary>
        public double DomainsTemperature { get; set; }

        /// <summary>
        /// Шлях до зовнішнього (зовнішньо знайденого) допустимого розкладу.
        /// Якщо задано, юніт стартує з нього замість конструктивного DFS
        /// (домени слотів НЕ звужуються: оператори далі можуть перепризначати слоти).
        /// </summary>
        public string SeedSchedule { get; set; }

        /// <summary>Діагностика скану мутацій (тимчасово).</summary>
        public bool MutationDiagnostics { get; set; }

        /// <summary>
        /// Кількість додаткових ITC-операторів (0 = вимкнено; 1 = op9; 2 = op9+op10).
        /// Старі оператори та їх цикл не змінюються при 0.
        /// </summary>
        public int ExtraItcOperators { get; set; }

        /// <summary>Максимум класів, що виселяються одним ITC-EVICT-CHAIN ходом.</summary>
        public int EvictChainMaxClasses { get; set; }

        /// <summary>
        /// Семантичний адаптер операторів: "series" (типово, поведінка AcaTime)
        /// або "itc" (цикл ITC-операторів: класовий скан, подвійний скан,
        /// eviction-chain). Типово series — AcaTime не змінюється.
        /// </summary>
        public string Adapter { get; set; }

        /// <summary>Ліміт вузлів backtracking-вставки у ITC-EVICT-CHAIN.</summary>
        public int EvictChainNodeCap { get; set; }

        /// <summary>Кількість незалежних цілей у opt-in room-aware eviction operator.</summary>
        public int EvictChainAttempts { get; set; }

        /// <summary>
        /// Кількість ітерацій алгоритму
        /// </summary>
        public int GeneticIterations { get; set; }

        /// <summary>
        /// Кількість незалежних стартових рішень, які будуть створені Default-алгоритмом.
        /// </summary>
        public int InitialPopulationSize { get; set; }

        /// <summary>
        /// Кількість блоків, які HGT переносить в одному offspring.
        /// </summary>
        public int HgtBlockCount { get; set; }

        /// <summary>
        /// Кількість HGT offspring-ів, які перевіряються перед Genetic-проходом.
        /// </summary>
        public int HgtAttempts { get; set; }

        public int KickAfterStagnation { get; set; }
        public int KickSeriesCount { get; set; }
        public int KickLocalIterations { get; set; }
        public int KickBranches { get; set; }
        public int KickTimeoutInSeconds { get; set; }
        public int PopulationBranches { get; set; }
        public int PopulationBranchIterations { get; set; }
        public int DeltaTransferIterations { get; set; }
        public int MaxParallelBranches { get; set; }
        public int IntraBranchPopulationSize { get; set; }
        public int OperationAttemptsPerIteration { get; set; }
        public int MutationTournamentAttempts { get; set; }
        public int MutationDomainCandidates { get; set; }
        public bool AdaptiveOperationPortfolio { get; set; }
        public double PopulationBranchMinimumScoreRatio { get; set; }
        public int PopulationBranchStagnationLimit { get; set; }
        public int DestroyRepairSeriesCount { get; set; }
        public int DestroyRepairMaxMilliseconds { get; set; }
        public int DestroyRepairMaxAcceptedLoss { get; set; }
        public int DestroyRepairAttempts { get; set; }

        /// <summary>
        /// Кількість легких мутацій для повторного спуску після прийнятої
        /// втрати destroy-repair. 0 = стара поведінка (без спуску).
        /// </summary>
        public int DestroyRepairRelocalIterations { get; set; }

        /// <summary>
        /// Скільки ітерацій без покращення запускають ILS-епізод
        /// (kick робочого базису в гірший басейн з наступним відновленням).
        /// 0 = вимкнено.
        /// </summary>
        public int IlsStagnationLimit { get; set; }

        /// <summary>
        /// Бюджет ітерацій на відновлення після ILS-kick.
        /// </summary>
        public int IlsRepairIterations { get; set; }

        /// <summary>
        /// Кількість серій у ILS-kick (розмір збурення).
        /// </summary>
        public int IlsKickSeriesCount { get; set; }

        /// <summary>
        /// Втрата, прийнятна для ILS-kick relocation серії (chain-relocate
        /// без воріт на покращення). 0 = старий TryPerturb-режим.
        /// </summary>
        public int IlsChainKickLoss { get; set; }

        /// <summary>
        /// Кількість послідовних chain-kick ходів в одному ILS-збуренні
        /// (кумулятивна втрата обмежена IlsChainKickLoss).
        /// </summary>
        public int IlsChainKickMoves { get; set; }

        /// <summary>
        /// Chain-relocate: true = турнірний скан (серія × домен × B, найкраща
        /// дельта), false = легаси-режим (випадкова серія, перше покращення).
        /// </summary>
        public bool ChainDirected { get; set; }

        /// <summary>
        /// Інтервал HGT-міграції (ітерацій): вікно прийнятих подій лідера
        /// реплається на laggard. 0 = вимкнено.
        /// </summary>
        public int HgtInterval { get; set; }

        /// <summary>
        /// Бюджет паралельних потоків: 0 = авто (ProcessorCount - 1, cgroup-aware),
        /// N = жорсткий ліміт (для deployment з обмеженими ресурсами).
        /// </summary>
        public int ParallelLineages { get; set; }

        /// <summary>
        /// Дешева інкрементальна оцінка користувацьких правил (чорна скірня,
        /// з верифікацією і відкатом на повну оцінку). false = класична повна оцінка.
        /// </summary>
        public bool CheapEvaluation { get; set; }

        /// <summary>
        /// FLAT-дзеркало генома: бітсети (teacher/group × day,pair), фаза 1a —
        /// телеметрія (диференціал з словниками, нуль змін поведінки).
        /// </summary>
        public bool FlatIndex { get; set; }

        /// <summary>FLAT-фаза 1b: precheck валідації з бітсетів (потрібен FlatIndex).</summary>
        public bool FlatIndexPrecheck { get; set; }

        /// <summary>
        /// Діагностика операцій: -1 = звичайний цикл (2:2:1:1:1);
        /// 0..6 = виконувати лише цю операцію (0,3=short, 1,4=long, 2=swap,
        /// 5=destroy, 6=chain-relocate).
        /// </summary>
        public int OnlyOperation { get; set; }


        public AlgorithmParams(Dictionary<string,string> parameters)
        {
            // встановлюємо значення параметрів з словника
            ResultsCount = parameters.ContainsKey("ResultsCount") ? int.Parse(parameters["ResultsCount"]) : 1;
            MaxIterations = parameters.ContainsKey("MaxIterations") ? int.Parse(parameters["MaxIterations"]) : 1000;
            TimeoutInSeconds = parameters.ContainsKey("TimeoutInSeconds") ? int.Parse(parameters["TimeoutInSeconds"]) : 60;
            SlotsTopK = parameters.ContainsKey("SlotsTopK") ? int.Parse(parameters["SlotsTopK"]) : 1;
            DomainsTopK = parameters.ContainsKey("DomainsTopK") ? int.Parse(parameters["DomainsTopK"]) : 1;
            SlotsTemperature = parameters.ContainsKey("SlotsTemperature") ? double.Parse(parameters["SlotsTemperature"], CultureInfo.InvariantCulture) : 1;
            DomainsTemperature = parameters.ContainsKey("DomainsTemperature") ? double.Parse(parameters["DomainsTemperature"], CultureInfo.InvariantCulture) : 1;
            SeedSchedule = parameters.ContainsKey("SeedSchedule") ? parameters["SeedSchedule"] : null;
            MutationDiagnostics = parameters.ContainsKey("MutationDiagnostics") && parameters["MutationDiagnostics"] == "1";
            ExtraItcOperators = parameters.ContainsKey("ExtraItcOperators") ? int.Parse(parameters["ExtraItcOperators"]) : 0;
            EvictChainMaxClasses = parameters.ContainsKey("EvictChainMaxClasses") ? int.Parse(parameters["EvictChainMaxClasses"]) : 8;
            Adapter = parameters.ContainsKey("Adapter") ? parameters["Adapter"] : "series";
            EvictChainNodeCap = parameters.ContainsKey("EvictChainNodeCap") ? int.Parse(parameters["EvictChainNodeCap"]) : 400;
            EvictChainAttempts = parameters.ContainsKey("EvictChainAttempts") ? int.Parse(parameters["EvictChainAttempts"]) : 4;

            GeneticIterations = parameters.ContainsKey("GeneticIterations") ? int.Parse(parameters["GeneticIterations"]) : 100;
            InitialPopulationSize = parameters.ContainsKey("InitialPopulationSize")
                ? int.Parse(parameters["InitialPopulationSize"])
                : Math.Max(1, ResultsCount);
            HgtBlockCount = parameters.ContainsKey("HgtBlockCount")
                ? int.Parse(parameters["HgtBlockCount"])
                : 1;
            HgtAttempts = parameters.ContainsKey("HgtAttempts")
                ? int.Parse(parameters["HgtAttempts"])
                : 1;
            KickAfterStagnation = parameters.ContainsKey("KickAfterStagnation")
                ? int.Parse(parameters["KickAfterStagnation"])
                : 6;
            KickSeriesCount = parameters.ContainsKey("KickSeriesCount")
                ? int.Parse(parameters["KickSeriesCount"])
                : 2;
            KickLocalIterations = parameters.ContainsKey("KickLocalIterations")
                ? int.Parse(parameters["KickLocalIterations"])
                : 8;
            KickBranches = parameters.ContainsKey("KickBranches")
                ? int.Parse(parameters["KickBranches"])
                : 0;
            KickTimeoutInSeconds = parameters.ContainsKey("KickTimeoutInSeconds")
                ? int.Parse(parameters["KickTimeoutInSeconds"])
                : 20;
            PopulationBranches = parameters.ContainsKey("PopulationBranches")
                ? int.Parse(parameters["PopulationBranches"])
                : 1;
            PopulationBranchIterations = parameters.ContainsKey("PopulationBranchIterations")
                ? int.Parse(parameters["PopulationBranchIterations"])
                : 25;
            DeltaTransferIterations = parameters.ContainsKey("DeltaTransferIterations")
                ? int.Parse(parameters["DeltaTransferIterations"])
                : PopulationBranchIterations;
            MaxParallelBranches = parameters.ContainsKey("MaxParallelBranches")
                ? int.Parse(parameters["MaxParallelBranches"])
                : 1;
            IntraBranchPopulationSize = parameters.ContainsKey("IntraBranchPopulationSize")
                ? int.Parse(parameters["IntraBranchPopulationSize"])
                : 1;
            OperationAttemptsPerIteration = parameters.ContainsKey("OperationAttemptsPerIteration")
                ? int.Parse(parameters["OperationAttemptsPerIteration"])
                : 1;
            MutationTournamentAttempts = parameters.ContainsKey("MutationTournamentAttempts")
                ? int.Parse(parameters["MutationTournamentAttempts"])
                : 3;
            MutationDomainCandidates = parameters.ContainsKey("MutationDomainCandidates")
                ? int.Parse(parameters["MutationDomainCandidates"])
                : 8;
            AdaptiveOperationPortfolio = parameters.ContainsKey("AdaptiveOperationPortfolio")
                ? bool.Parse(parameters["AdaptiveOperationPortfolio"])
                : true;
            PopulationBranchMinimumScoreRatio = parameters.ContainsKey("PopulationBranchMinimumScoreRatio")
                ? double.Parse(parameters["PopulationBranchMinimumScoreRatio"], CultureInfo.InvariantCulture)
                : 0.85;
            PopulationBranchStagnationLimit = parameters.ContainsKey("PopulationBranchStagnationLimit")
                ? int.Parse(parameters["PopulationBranchStagnationLimit"])
                : 8;
            DestroyRepairSeriesCount = parameters.ContainsKey("DestroyRepairSeriesCount")
                ? int.Parse(parameters["DestroyRepairSeriesCount"])
                : 2;
            DestroyRepairMaxMilliseconds = parameters.ContainsKey("DestroyRepairMaxMilliseconds")
                ? int.Parse(parameters["DestroyRepairMaxMilliseconds"])
                : 300;
            DestroyRepairMaxAcceptedLoss = parameters.ContainsKey("DestroyRepairMaxAcceptedLoss")
                ? int.Parse(parameters["DestroyRepairMaxAcceptedLoss"])
                : 1000;
            DestroyRepairAttempts = parameters.ContainsKey("DestroyRepairAttempts")
                ? int.Parse(parameters["DestroyRepairAttempts"])
                : 3;
            DestroyRepairRelocalIterations = parameters.ContainsKey("DestroyRepairRelocalIterations")
                ? int.Parse(parameters["DestroyRepairRelocalIterations"])
                : 0;

            IlsStagnationLimit = parameters.ContainsKey("IlsStagnationLimit")
                ? int.Parse(parameters["IlsStagnationLimit"])
                : 12;
            IlsRepairIterations = parameters.ContainsKey("IlsRepairIterations")
                ? int.Parse(parameters["IlsRepairIterations"])
                : 20;
            IlsKickSeriesCount = parameters.ContainsKey("IlsKickSeriesCount")
                ? int.Parse(parameters["IlsKickSeriesCount"])
                : 2;
            IlsChainKickLoss = parameters.ContainsKey("IlsChainKickLoss")
                ? int.Parse(parameters["IlsChainKickLoss"])
                : 0;
            IlsChainKickMoves = parameters.ContainsKey("IlsChainKickMoves")
                ? int.Parse(parameters["IlsChainKickMoves"])
                : 1;
            ChainDirected = parameters.ContainsKey("ChainDirected")
                ? bool.Parse(parameters["ChainDirected"])
                : true;
            HgtInterval = parameters.ContainsKey("HgtInterval")
                ? int.Parse(parameters["HgtInterval"])
                : 0;
            ParallelLineages = parameters.ContainsKey("ParallelLineages")
                ? int.Parse(parameters["ParallelLineages"])
                : 0;

            CheapEvaluation = parameters.ContainsKey("CheapEvaluation") ? bool.Parse(parameters["CheapEvaluation"]) : false;
            FlatIndex = parameters.ContainsKey("FlatIndex") ? bool.Parse(parameters["FlatIndex"]) : false;
            FlatIndexPrecheck = parameters.ContainsKey("FlatIndexPrecheck") ? bool.Parse(parameters["FlatIndexPrecheck"]) : false;

            OnlyOperation = parameters.ContainsKey("OnlyOperation")
                ? int.Parse(parameters["OnlyOperation"])
                : -1;

        }

    }

}
