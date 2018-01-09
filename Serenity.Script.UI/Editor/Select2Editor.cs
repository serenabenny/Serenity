﻿using jQueryApi;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Serenity
{
    [Imported, Serializable]
    public class Select2Item
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public object Source { get; set; }
        public bool Disabled { get; set; }
    }

    [Imported(ObeysTypeSystem = true)]
    [Element("<input type=\"hidden\"/>"), IncludeGenericArguments(false), ScriptName("Select2Editor")]
    public abstract class Select2Editor<TOptions, TItem> : Widget<TOptions>, ISetEditValue, IGetEditValue, IStringValue, IReadOnly
        where TOptions : class, new()
        where TItem: class
    {
        protected bool multiple;
        protected List<Select2Item> items;
        protected JsDictionary<string, Select2Item> itemById;
        protected int pageSize = 100;
        protected string lastCreateTerm;

        static Select2Editor()
        {
            Q.Prop(typeof(Select2Editor<object, object>), "value");
            Q.Prop(typeof(Select2Editor<object, object>), "values");
            Q.Prop(typeof(Select2Editor<object, object>), "readOnly");
        }

        public Select2Editor(jQueryObject hidden, TOptions opt)
            : base(hidden, opt)
        {
            items = new List<Select2Item>();
            itemById = new JsDictionary<string, Select2Item>();

            var emptyItemText = EmptyItemText();
            if (emptyItemText != null)
                hidden.Attribute("placeholder", emptyItemText);

            var select2Options = GetSelect2Options();
            multiple = Q.IsTrue(select2Options.Multiple);
            hidden.Select2(select2Options);
            
            hidden.Attribute("type", "text"); // jquery validate to work
            hidden.Bind2("change." + this.uniqueName, (e, x) =>
            {
                if (e.HasOriginalEvent() || Q.IsFalse(x))
                {
                    if (hidden.GetValidator() != null)
                        hidden.Valid();
                }
            });
        }

        public override void Destroy()
        {
            if (element != null)
                element.Select2("destroy");
            base.Destroy();
        }


        protected virtual string EmptyItemText()
        {
            return element.GetAttribute("placeholder") ?? Q.Text("Controls.SelectEditor.EmptyItemText");
        }

        protected virtual Select2Options GetSelect2Options()
        {
            var emptyItemText = EmptyItemText();

            return new Select2Options
            {
                Data = items,
                PlaceHolder = !emptyItemText.IsEmptyOrNull() ? emptyItemText : null,
                AllowClear = emptyItemText != null,
                CreateSearchChoicePosition = "bottom",
                Query = delegate(Select2QueryOptions query)
                {
                    var term = query.Term.IsEmptyOrNull() ? "" : Q.Externals.StripDiacritics(query.Term ?? "").ToUpperCase();
                    var results = this.items.Filter(item =>
                    {
                        return (term == null ||
                            Q.Externals.StripDiacritics(item.Text ?? "").ToUpperCase().StartsWith(term));
                    });

                    results.AddRange(this.items.Filter(item =>
                    {
                        return term != null &&
                            !Q.Externals.StripDiacritics(item.Text ?? "").ToUpperCase().StartsWith(term) &&
                            Q.Externals.StripDiacritics(item.Text ?? "").ToUpperCase().IndexOf(term) >= 0;
                    }));

                    query.Callback(new Select2Result
                    {
                        Results = results.Slice((query.Page - 1) * pageSize, query.Page * pageSize),
                        More = results.Count >= query.Page * pageSize
                    });
                },
                InitSelection = delegate(jQueryObject element, Action<object> callback)
                {
                    var val = element.GetValue();
                    var isAutoComplete = this.IsAutoComplete();

                    if (multiple)
                    {
                        var list = new List<object>();
                        foreach (var z in val.Split(","))
                        {
                            var item = itemById[z];

                            if (item == null && isAutoComplete)
                            {
                                item = new Select2Item { Id = z, Text = z };
                                AddItem(item);
                            }

                            if (item != null)
                                list.Add(item);
                        }

                        callback(list);
                        return;
                    }

                    var it = itemById[val];
                    if (it == null && isAutoComplete)
                    {
                        it = new Select2Item { Id = val, Text = val };
                        AddItem(it);
                    }

                    callback(it);
                }
            };
        }

        public bool Delimited
        {
            get
            {
                return Q.IsTrue(options.As<dynamic>().delimited);
            }
        }

        protected void ClearItems()
        {
            this.items.Clear();
            this.itemById = new JsDictionary<string, Select2Item>();
        }

        [ScriptName("addItem")]
        protected void AddItem(Select2Item item)
        {
            this.items.Add(item);
            this.itemById[item.Id] = item;
        }

        [ScriptName("addOption")]
        protected void AddItem(string key, string text, TItem source = null, bool disabled = false)
        {
            AddItem(new Select2Item
            {
                Id = key,
                Text = text,
                Source = source,
                Disabled = disabled
            });
        }

        protected void AddInplaceCreate(string addTitle = null, string editTitle = null)
        {
            var self = this;

            addTitle = addTitle ?? Q.Text("Controls.SelectEditor.InplaceAdd");
            editTitle = editTitle ?? Q.Text("Controls.SelectEditor.InplaceEdit");

            var inplaceButton = J("<a><b/></a>").AddClass("inplace-button inplace-create")
                .Attribute("title", addTitle)
                .InsertAfter(this.element)
                .Click(e =>
                {
                    self.InplaceCreateClick(e);
                });

            this.Select2Container.Add(this.element).AddClass("has-inplace-button");

            this.Change(e =>
            {
                bool isNew = this.multiple || this.Value.IsEmptyOrNull();
                inplaceButton
                    .Attribute("title", isNew ? addTitle : editTitle)
                    .ToggleClass("edit", !isNew);
            });

            this.ChangeSelect2(e =>
            {
                if (this.multiple)
                {
                    var values = this.Values;
                    if (values.Length > 0 && values[values.Length - 1] == Int32.MinValue.ToString())
                    {
                        this.Values = values.Slice(0, values.Length - 1);
                        InplaceCreateClick(e);
                    }
                }
                else if (this.Value == Int32.MinValue.ToString())
                {
                    this.Value = null;
                    InplaceCreateClick(e);
                }
            });

            if (this.multiple)
            {
                this.Select2Container.On("dblclick." + this.uniqueName, ".select2-search-choice", e =>
                {
                    var q = J(e.Target);
                    if (!q.HasClass("select2-search-choice"))
                        q = q.Closest(".select2-search-choice");
                    var index = q.Index();
                    var values = this.Values;
                    if (index < 0 || index >= this.Values.Length)
                        return;

                    e.As<JsDictionary<string, object>>()["editItem"] = values[index];
                    InplaceCreateClick(e);
                });
            }
        }

        protected virtual void InplaceCreateClick(jQueryEvent e)
        {
        }

        protected virtual bool IsAutoComplete()
        {
            return false;
        }

        public Func<string, object> GetCreateSearchChoice(
            Func<TItem, string> getName = null)
        {
            return s =>
            {
                lastCreateTerm = s;
                s = (Q.Externals.StripDiacritics(s) ?? "").ToLower();

                if (s.IsTrimmedEmpty())
                    return null;

                if (Q.Any(this.Items, x =>
                {
                    var text = getName != null ? getName(x.Source.As<TItem>()) : x.Text;
                    return Q.Externals.StripDiacritics(text ?? "").ToLower() == s;
                }))
                {
                    return null;
                }

                if (!Q.Any(this.Items, x => (Q.Externals.StripDiacritics(x.Text) ?? "").ToLower().Contains(s)))
                {
                    if (IsAutoComplete())
                        return new Select2Item
                        {
                            Id = lastCreateTerm,
                            Text = lastCreateTerm,
                        };

                    return new Select2Item
                    {
                        Id = Int32.MinValue.ToString(),
                        Text = Q.Text("Controls.SelectEditor.NoResultsClickToDefine")
                    };
                }

                if (IsAutoComplete())
                    return new Select2Item
                    {
                        Id = lastCreateTerm,
                        Text = lastCreateTerm
                    };

                return new Select2Item
                {
                    Id = Int32.MinValue.ToString(),
                    Text = Q.Text("Controls.SelectEditor.ClickToDefine")
                };
            };
        }

        public void SetEditValue(dynamic source, PropertyItem property)
        {
            object val = source[property.Name];
            if (Q.IsArray(val))
                Values = val.As<string[]>();
            else
                Value = val == null ? null : val.ToString();
        }

        public void GetEditValue(PropertyItem property, dynamic target)
        {
            if ((!multiple || Delimited))
                target[property.Name] = Value;
            else
                target[property.Name] = Values;
        }

        protected jQueryObject Select2Container
        {
            get { return this.element.PrevAll(".select2-container"); }
        }

        public List<Select2Item> Items
        {
            get { return this.items; }
        }

        public JsDictionary<string, Select2Item> ItemByKey
        {
            get { return this.itemById;}
        }

        public string Value
        {
            get
            {
                var val = this.element.Select2Get("val");

                if (val != null && Q.IsArray(val))
                    return ((string[])val).Join(",");

                return val as string;
            }
            set
            {
                if (value != Value)
                {
                    object val = value;

                    if (!string.IsNullOrEmpty(value) && multiple)
                    {
                        val = value.Split(',')
                            .Map(x => x.TrimToNull())
                            .Filter(x => x != null);
                    }

                    this.element.Select2("val", val).TriggerHandler("change", new object[] { true });
                }
            }
        }

        public string[] Values
        {
            get
            {
                var val = this.element.Select2Get("val");
                if (val == null)
                    return new string[0];

                if (Q.IsArray(val))
                    return ((string[])val);

                var str = val as string;
                if (string.IsNullOrEmpty(str))
                    return new string[0];

                return new string[] { str };
            }
            set
            {
                if (value == null || value.Length == 0)
                {
                    Value = null;
                    return;
                }

                Value = string.Join(",", value);
            }
        }

        public string Text
        {
            get
            {
                return ((dynamic)element.Select2Get("data") ?? new object()).text;
            }
        }

        public bool ReadOnly
        {
            get
            {
                return !string.IsNullOrEmpty(this.element.GetAttribute("readonly"));
            }
            set
            {
                if (value != ReadOnly)
                {
                    EditorUtils.SetReadOnly(this.element, value);
                    this.element.NextAll(".inplace-create")
                        .Attribute("disabled", value ? "disabled" : "")
                        .CSS("opacity", value ? "0.1" : "")
                        .CSS("cursor", value ? "default" : "");
                }
            }
        }
    }
}