using System.ComponentModel;
using System.Runtime.CompilerServices;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    public sealed partial class MultiRoutePaymentDraftRow : INotifyPropertyChanged
    {
        private readonly decimal[] _amounts = new decimal[11];
        private bool _minimumWageSupportEnabled = true;
        private decimal _minimumWageSupport;
        private TransportationProgramType _program = TransportationProgramType.SpecialEducation;
        private decimal _contractAmount;
        private bool _vatExempt;
        private ProgressPaymentCalculationConstants _constants = ProgressPaymentCalculationConstants.Defaults;
        private decimal[]? _acceptedAmounts;
        private decimal _acceptedSupport;
        private bool _acceptedSupportEnabled;
        private string _acceptedName = string.Empty;
        private string _routeName = string.Empty;
        public bool HasChanges => _acceptedAmounts is not null &&
            (!_amounts.SequenceEqual(_acceptedAmounts) || _minimumWageSupport != _acceptedSupport ||
             _minimumWageSupportEnabled != _acceptedSupportEnabled || RouteName != _acceptedName || PeriodChangesDiffer());
        private bool _hasSource;
        private string _sourceName = string.Empty;
        private decimal _sourcePrice, _sourceGuidePrice;
        public void ReconcileSource(string name, decimal price, decimal guidePrice)
        {
            if (_hasSource)
            {
                void Merge(int index, decimal oldSource, decimal nextSource, Action<decimal> set)
                {
                    if (oldSource == nextSource) return;
                    var edited = _acceptedAmounts is not null && _amounts[index] != _acceptedAmounts[index];
                    if (!edited) set(nextSource);
                    if (_acceptedAmounts is not null) _acceptedAmounts[index] = nextSource;
                }
                // Work days belong to the monthly hybrid draft, never to route master data.
                if (!HasPeriodChanges) Merge(1, _sourcePrice, price, value => DailyPrice = value);
                if (!HasPeriodChanges) Merge(5, _sourceGuidePrice, guidePrice, value => GuideDailyPrice = value);
                if (_sourceName != name)
                {
                    if (RouteName == _acceptedName) RouteName = name;
                    _acceptedName = name;
                }
            }
            _sourceName = name; _sourcePrice = price; _sourceGuidePrice = guidePrice;
            _hasSource = true;
            Changed(nameof(HasChanges));
        }
        public void AcceptChanges()
        {
            _acceptedAmounts = (decimal[])_amounts.Clone();
            _acceptedSupport = _minimumWageSupport;
            _acceptedSupportEnabled = _minimumWageSupportEnabled;
            _acceptedName = RouteName;
            AcceptPeriodChanges();
            Changed(nameof(HasChanges));
        }
        public void LoadInput(ProgressPaymentInput input, bool minimumWageSupportEnabled)
        {
            _transportPeriods = null; _guidePeriods = null;
            WorkDays = input.WorkDays; DailyPrice = input.DailyPrice;
            DiscountAmount = input.DiscountAmount; PriceDifference = input.PriceDifference;
            GuideWorkDays = input.GuideWorkDays; GuideDailyPrice = input.GuideDailyPrice;
            GuideDiscountAmount = input.GuideDiscountAmount; GuidePriceDifference = input.GuidePriceDifference;
            TaxDebt = input.TaxDebt; SocialSecurityDebt = input.SocialSecurityDebt;
            InspectionPenalty = input.InspectionPenalty;
            MinimumWageSupportEnabled = minimumWageSupportEnabled;
            MinimumWageSupport = input.MinimumWageSupport;
            _transportPeriods = input.TransportPeriods?.ToArray(); _guidePeriods = input.GuidePeriods?.ToArray();
            _hasPeriodChanges = input.HasPeriodChanges;
            Changed(string.Empty);
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        public string RouteKey { get; set; } = string.Empty;
        private string _routeNumber = string.Empty;
        public string RouteNumber { get => _routeNumber; set { if (_routeNumber == value) return; _routeNumber = value; PropertyChanged?.Invoke(this,new(nameof(RouteDisplayText))); } }
        public string RouteName { get => _routeName; set { if (_routeName == value) return; _routeName = value; Changed(); PropertyChanged?.Invoke(this, new(nameof(RouteDisplayText))); } }
        public string RouteDisplayText => $"Hat {RouteNumber} — {RouteName}";
        public decimal WorkDays { get => _amounts[0]; set { if (_amounts[0] == value) return; _amounts[0] = value; Changed(); } }
        public decimal DailyPrice { get => _amounts[1]; set { if (_amounts[1] == value) return; _amounts[1] = value; Changed(); } }
        public decimal DiscountAmount { get => _amounts[2]; set { if (_amounts[2] == value) return; _amounts[2] = value; Changed(); } }
        public decimal PriceDifference { get => _amounts[3]; set { if (_amounts[3] == value) return; _amounts[3] = value; Changed(); } }
        public decimal GuideWorkDays { get => _amounts[4]; set { if (_amounts[4] == value) return; _amounts[4] = value; Changed(); } }
        public decimal GuideDailyPrice { get => _amounts[5]; set { if (_amounts[5] == value) return; _amounts[5] = value; Changed(); } }
        public decimal GuideDiscountAmount { get => _amounts[6]; set { if (_amounts[6] == value) return; _amounts[6] = value; Changed(); } }
        public decimal GuidePriceDifference { get => _amounts[7]; set { if (_amounts[7] == value) return; _amounts[7] = value; Changed(); } }
        public decimal TaxDebt { get => _amounts[8]; set { if (_amounts[8] == value) return; _amounts[8] = value; Changed(); } }
        public decimal SocialSecurityDebt { get => _amounts[9]; set { if (_amounts[9] == value) return; _amounts[9] = value; Changed(); } }
        public decimal InspectionPenalty { get => _amounts[10]; set { if (_amounts[10] == value) return; _amounts[10] = value; Changed(); } }
        public bool MinimumWageSupportEnabled
        {
            get => _minimumWageSupportEnabled;
            set
            {
                if (_minimumWageSupportEnabled == value) return;
                _minimumWageSupportEnabled = value;
                if (!value) _minimumWageSupport = 0m;
                Changed(); PropertyChanged?.Invoke(this, new(nameof(MinimumWageSupport)));
            }
        }
        public decimal MinimumWageSupport
        {
            get => _minimumWageSupport;
            set
            {
                if (value != 0m && !MinimumWageSupportEnabled) MinimumWageSupportEnabled = true;
                if (_minimumWageSupport == value) return;
                _minimumWageSupport = value;
                Changed();
            }
        }
        public void Configure(TransportationProgramType program, decimal contractAmount, bool vatExempt, ProgressPaymentCalculationConstants constants)
        {
            _program=program; _contractAmount=contractAmount; _vatExempt=vatExempt; _constants=constants;
            Changed(string.Empty);
        }
        public ProgressPaymentInput CreateInput() => new(WorkDays,DailyPrice,DiscountAmount,PriceDifference,
            _contractAmount,_vatExempt,TaxDebt,SocialSecurityDebt,MinimumWageSupportEnabled?MinimumWageSupport:0m,
            InspectionPenalty,_program==TransportationProgramType.SpecialEducation?GuideWorkDays:0m,
            _program==TransportationProgramType.SpecialEducation?GuideDailyPrice:0m,
            _program==TransportationProgramType.SpecialEducation?GuideDiscountAmount:0m,
            _program==TransportationProgramType.SpecialEducation?GuidePriceDifference:0m,_program,
            _transportPeriods, _program==TransportationProgramType.SpecialEducation?_guidePeriods:null, HasPeriodChanges);
        public ProgressPaymentCalculation? Preview
        {
            get
            {
                try { return PaymentScreenModel.Calculate(CreateInput(),_constants); }
                catch(Exception ex) when(ex is ArgumentException or OverflowException) { return null; }
            }
        }
        public string NetPayableText => Preview is {} result ? result.NetPayable.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+" ₺" : "Girdiyi kontrol edin";
        private void Changed([CallerMemberName] string? property=null)
        {
            PropertyChanged?.Invoke(this,new(property));
            PropertyChanged?.Invoke(this,new(nameof(NetPayableText)));
        }
    }
}
