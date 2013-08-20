//-----------------------------------------------------------------------------------------------------------------------
namespace EventAggregator
{
    public interface IEventAggregator
    {
        void Subscribe(object subscriber);
        void Publish<TEvent>(TEvent eventToPublish);
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace EventAggregator
{
    public interface ISubscriber<in T>
    {
        void OnEvent(T e);
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace EventAggregator
{
    public class SimpleEventAggregator : IEventAggregator
    {
        private readonly Dictionary<Type, List<WeakReference>> _eventSubscriberLists = new Dictionary<Type, List<WeakReference>>();
        private readonly object _lock = new object();

        public void Subscribe(object subscriber)
        {
            lock (_lock)
            {
                var subscriberTypes = subscriber.GetType().GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISubscriber<>));

                var weakReference = new WeakReference(subscriber);

                foreach (var subscriberType in subscriberTypes)
                {
                    var subscribers = GetSubscribers(subscriberType);
                    subscribers.Add(weakReference);
                }
            }
        }

        public void Publish<TEvent>(TEvent eventToPublish)
        {
            var subscriberType = typeof(ISubscriber<>).MakeGenericType(typeof(TEvent));
            var subscribers = GetSubscribers(subscriberType);
            var subscribersToRemove = new List<WeakReference>();

            foreach (var weakSubscriber in subscribers)
            {
                if (weakSubscriber.IsAlive)
                {
                    var subscriber = (ISubscriber<TEvent>)weakSubscriber.Target;
                    var syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

                    syncContext.Post(s => subscriber.OnEvent(eventToPublish), null);
                }
                else
                {
                    subscribersToRemove.Add(weakSubscriber);
                }
            }

            if (subscribersToRemove.Any())
            {
                lock (_lock)
                {
                    foreach (var remove in subscribersToRemove)
                        subscribers.Remove(remove);
                }
            }
        }

        private List<WeakReference> GetSubscribers(Type subscriberType)
        {
            List<WeakReference> subscribers;

            lock (_lock)
            {
                var found = _eventSubscriberLists.TryGetValue(subscriberType, out subscribers);

                if (!found)
                {
                    subscribers = new List<WeakReference>();
                    _eventSubscriberLists.Add(subscriberType, subscribers);
                }
            }

            return subscribers;
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace Models.Enums
{
    public enum BooleanAnswerType
    {
        Radio,
        Check
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace Models.Event
{
    public class PromptActivated 
    {
        public Prompt Prompt { get; set; }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace Models.Event
{
    public class PromptDeactivated
    {
        public Prompt Prompt { get; set; }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace Models.Event
{
    public class PromptEvent
    {
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class BooleanAnswer : Prompt
    {
        public bool True 
        { 
            get
            {
                return Value == "true";
            }

            set
            {
                Value = value ? "true" : "false";
            }
        }

        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}{1}", new string('\t', leftFormat), Text);
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
namespace Models
{
    public class BusinessRule
    {
        public bool IsValid
        {
            get { throw new System.NotImplementedException(); }
            set { throw new System.NotImplementedException(); }
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;

namespace Models
{
    public abstract class CompositePrompt : Prompt
    {
        public List<Prompt> Prompts { get; set; }

        protected CompositePrompt()
        {
            Prompts = new List<Prompt>();
        }

        public override void Show(int leftFormat)
        {
            foreach (var prompt in Prompts)
            {
                prompt.Show(leftFormat);
            }
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class HelpLink : CompositePrompt
    {
        public string Title
        {
            get { return Text; }
            set { Text = value; }
        }

        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}'{1}'", new string('\t', leftFormat), Text);

            base.Show(++leftFormat);
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class InformationalText : Prompt
    {
        public bool IsBold { get; set; }

        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}Info: {1}'{2}'{3}", new string('\t', leftFormat), IsBold ? "<b>" : string.Empty, Text, IsBold ? "</b>" : string.Empty);
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class Note : Prompt
    {
        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}<b>Note:</b> {1}", new string('\t', leftFormat), Text);
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using EventAggregator;
using Models.Event;

namespace Models
{
    public abstract class Prompt : ISubscriber<PromptDeactivated>
    {
        # region properties

        public Dictionary<Type, List<Prompt>> ActiveDependencies = new Dictionary<Type, List<Prompt>>();

        public Questionnaire Questionnaire;

        public virtual string Value { get; set; }
        public string Text { get; set; }
        public string Description { get; set; }

        public bool IsActive
        {
            get { return _isActive; }
            
            set
            {
                _isActive = value;

                if (_isActive)
                    Questionnaire.EventAggregator.Publish(new PromptActivated { Prompt = this });
                else
                    Questionnaire.EventAggregator.Publish(new PromptDeactivated { Prompt = this });
            }
        }

        public readonly List<BusinessRule> BusinessRules = new List<BusinessRule>();

        public bool IsValid
        {
            get
            {
                foreach (var rule in BusinessRules)
                {
                    if (!rule.IsValid) return false;
                }

                return true;
            }
        }

        # endregion

        # region constructors

        protected Prompt(bool isActive = true)
        {
            _isActive = isActive;
        }

        # endregion

        # region methods

        public virtual void Show(int leftFormat)
        {
            if (!IsActive) return;

            Debug.WriteLine("{0}{1}", new string('\t', leftFormat), Text);
        }

        # endregion

        # region attributes

        private bool _isActive;

        # endregion

        # region event handlers

        public void OnEvent(PromptDeactivated e)
        {
            Debug.Print("{0} - Source: {1}", typeof(PromptDeactivated).Name, e.Prompt.Text);
            Debug.Print("{0} - Target: {1}", typeof(PromptDeactivated).Name, Text);

            // update state / take appropiate action according to the event type fired
            List<Prompt> promptsOfInterest;

            var found = ActiveDependencies.TryGetValue(e.GetType(), out promptsOfInterest);

            if (found && promptsOfInterest.Contains(e.Prompt))
            {
                IsActive = false;
            }
        }

        # endregion
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class Question : CompositePrompt
    {
        public int Number { get; set; }
        public bool IsMandatory { get; set; }

        public Question()
        {
            IsMandatory = true;
        }

        public string Title
        {
            get { return Text; }
            set { Text = value; }
        }

        public override void Show(int leftFormat)
        {
            if (!IsActive) return;

            Debug.WriteLine("{0}{1}{2}. {3}", new string('\t', leftFormat), 
                                                IsMandatory ? "*" : " ", 
                                                Number, 
                                                Title);
            base.Show(++leftFormat);
        }
    }
}

//------------
using System.Diagnostics;
using EventAggregator;

namespace Models
{
    public class Questionnaire : CompositePrompt
    {
        public IEventAggregator EventAggregator;

        public string Title
        {
            get { return Text; }
            set { Text = value; }
        }

        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}Title: '{1}'", new string('\t', leftFormat), Title);

            base.Show(++leftFormat);
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class SingleChoiceAnswer : CompositePrompt
    {
        public override void Show(int leftFormat)
        {
            foreach (var prompt in Prompts)
            {
                Debug.WriteLine("{0}<radio>{1}</radio>", new string('\t', leftFormat), prompt.Text);
            }
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Diagnostics;

namespace Models
{
    public class TextAnswer : Prompt
    {
        public int MaxLength { get; set; }

        public override void Show(int leftFormat)
        {
            Debug.WriteLine("{0}Answer: {1}", new string('\t', leftFormat), 
                                              MaxLength <= 100 ? "input" : "textarea");
        }
    }
}

//-----------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Diagnostics;
using EventAggregator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.Event;

namespace Models.Test
{
    [TestClass]
    public class ModelsTest
    {
        # region constructors

        public ModelsTest()
        {
            _ea = new SimpleEventAggregator();
        }

        # endregion

        # region tests

        [TestMethod]
        public void Test_Models_One()
        {
            Debug.Print("--- build questionnaire -----------------------------------------------------------------------------------");
            _exitSurvey = BuildExitSurveyQuestionnaire();

            Debug.Print("--- render complete questionnaire -----------------------------------------------------------------------------------");
            const int leftMargin = 0;
            _exitSurvey.Show(leftMargin);

            Debug.Print("--- wire dependencies -----------------------------------------------------------------------------------");
            WireDependencies();

            Debug.Print("--- update prompt properties -----------------------------------------------------------------------------------");
            UpdatePrompts();

            //Debug.Print("--- publish events -----------------------------------------------------------------------------------");
            //PublishEvents();

            Debug.Print("--- render questionnaire with wired dependencies -----------------------------------------------------------------------------------");
            _exitSurvey.Show(leftMargin);
        }

        # endregion

        # region helpers

        private Questionnaire BuildExitSurveyQuestionnaire()
        {
            var helpLink = GetHelpLink();

            var questionNumber = 0;
            var q1 = BuildQuestionOne(++questionNumber);
            var q2 = BuildQuestionTwo(++questionNumber);
            var q3 = BuildQuestionThree(++questionNumber);
            var q4 = BuildQuestionFourth(++questionNumber);

            return new Questionnaire
                        {
                            Title = "Family Relationship with your Employer",
                            Prompts =
                                    {
                                        helpLink,
                                        q1,
                                        q2,
                                        q3,
                                        q4
                                    },
                            EventAggregator = _ea
                        };
        }

        private void WireDependencies()
        {
            // get source
            var q2 = _exitSurvey.Prompts[2] as Question;
            var singleChoiceAnswer = q2.Prompts[0] as SingleChoiceAnswer;
            var firstBooleanAnswer = singleChoiceAnswer.Prompts[0];

            //
            firstBooleanAnswer.Questionnaire = _exitSurvey;

            //
            var q3 = _exitSurvey.Prompts[3];
            q3.ActiveDependencies.Add(typeof(PromptDeactivated), new List<Prompt>
                                                                        {
                                                                            firstBooleanAnswer
                                                                        });
            _ea.Subscribe(q3);

            // because q3 will Publish in its OnEvent
            q3.Questionnaire = _exitSurvey;

            //
            var q4 = _exitSurvey.Prompts[4];
            q4.ActiveDependencies.Add(typeof(PromptDeactivated), new List<Prompt>
                                                                        {
                                                                            q3
                                                                        });
            _ea.Subscribe(q4);

            q4.Questionnaire = _exitSurvey;
        }

        private void PublishEvents()
        {
            var q2 = _exitSurvey.Prompts[2] as Question;
            var singleChoiceAnswer = q2.Prompts[0] as SingleChoiceAnswer;

            var firstBooleanAnswer = singleChoiceAnswer.Prompts[0];
            var secondBooleanAnswer = singleChoiceAnswer.Prompts[1];

            _ea.Publish(new PromptDeactivated
                        {
                            Prompt = firstBooleanAnswer
                        });
        }

        private void UpdatePrompts()
        {
            var q2 = _exitSurvey.Prompts[2] as Question;
            var singleChoiceAnswer = q2.Prompts[0] as SingleChoiceAnswer;

            var firstBooleanAnswer = singleChoiceAnswer.Prompts[0];
            firstBooleanAnswer.IsActive = false;
        }

        private static HelpLink GetHelpLink()
        {
            var rulingFromCRA = new InformationalText
                {
                    Text = "Ruling from CRA",
                    IsBold = true
                };

            var rulingFromCRADescription = new InformationalText
                {
                    Text = "A ruling is an official decision issued by an authorized officer of the CRA."
                };

            var appealToCRA = new InformationalText
                {
                    Text = "Appeal to the CRA, Tax Court or Federal Court",
                    IsBold = true
                };

            var appealToCRADescription = new InformationalText
                {
                    Text = "If the appeal is not finalized, provide the decision being appealed."
                };

            var helpLink = new HelpLink
                                {
                                    Title = "Help for this page",
                                    Prompts =
                                        {
                                            rulingFromCRA,
                                            rulingFromCRADescription,
                                            appealToCRA,
                                            appealToCRADescription
                                        }
                                };

            return helpLink;
        }

        private static Question BuildQuestionOne(int questionNumber)
        {
            var note = new Note
            {
                Text = "Provide the name of the business not the name of the individual."
            };

            var textAnswer = new TextAnswer
            {
                MaxLength = 500
            };

            var q = new Question
            {
                Title = "To which employer are you related?",
                Number = questionNumber,
                Prompts =   { 
                                note, 
                                textAnswer 
                            }
            };

            return q;
        }

        private static Question BuildQuestionTwo(int questionNumber)
        {
            var singleChoiceAnswer = new SingleChoiceAnswer
                        {
                            Prompts =   { 
                                            new BooleanAnswer { Text = "my father, mother, grandparent or great-grandparent (including adoptive, step and in-law)" },
                                            new BooleanAnswer { Text = "my brother or sister (including step or in-law)" },
                                            new BooleanAnswer { Text = "my son, daughter, grandchild or great-grandchild (including adoptive, step and in-law)" },
                                            new BooleanAnswer { Text = "my spouse (including common law)" },
                                            new BooleanAnswer { Text = "my aunt, uncle, niece, nephew or cousin" }
                                        }
                        };

            var q = new Question
            {
                Title = "The business is owned by:",
                IsMandatory = false,
                Number = questionNumber,
                Prompts =   { 
                                singleChoiceAnswer 
                            }
            };

            return q;
        }

        private static Question BuildQuestionThree(int questionNumber)
        {
            var singleChoiceAnswer = new SingleChoiceAnswer
            {
                Prompts =   { 
                                new BooleanAnswer { Text = "Yes" },
                                new BooleanAnswer { Text = "No" }
                            }
            };

            var q = new Question
            {
                Title = "Was there an insurability ruling made in the last three years by the Canada Revenue Agency (CRA) for this employment?",
                Number = questionNumber,
                Prompts = { singleChoiceAnswer }
            };

            return q;
        }

        private static Question BuildQuestionFourth(int questionNumber)
        {
            var singleChoiceAnswer = new SingleChoiceAnswer
            {
                Prompts =   { 
                                new BooleanAnswer { Text = "The employment was insurable" },
                                new BooleanAnswer { Text = "The employment was not insurable" },
                                new BooleanAnswer { Text = "No decision has been made yet" }
                            }
            };

            var note = new Note
            {
                Text = "If the CRA insurability decision is currently under appeal, refer to the help text."
            };

            var q = new Question
            {
                Title = "What was the decision on the insurability of this employment?",
                Number = questionNumber,
                Prompts = { singleChoiceAnswer, note }
            };

            return q;
        }

        # endregion

        # region attributes

        private readonly IEventAggregator _ea;

        private Questionnaire _exitSurvey;

        # endregion
    }
}

//-----------------------------------------------------------------------------------------------------------------------

//-----------------------------------------------------------------------------------------------------------------------

//-----------------------------------------------------------------------------------------------------------------------

//-----------------------------------------------------------------------------------------------------------------------

//-----------------------------------------------------------------------------------------------------------------------

//-----------------------------------------------------------------------------------------------------------------------
