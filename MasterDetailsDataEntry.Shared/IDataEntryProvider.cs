﻿using System;
using System.Collections.Generic;
using System.Text;

namespace MasterDetailsDataEntry.Shared
{
    public interface IDataEntryProvider
    {
        // Fields
        IEnumerable<DataField> GetFormFields(IModelDefinitionForm form);
        IEnumerable<DataField> GetFormFields(Type formType);
        Tuple<IEnumerable<DataField>, IEnumerable<DataField>> GetFormFields(IMultiModelDefinitionForm form);
        Tuple<IEnumerable<DataField>, IEnumerable<DataField>> GetMultiFormFields(Type formType);

        // Data
        System.Collections.IList GetModelData(IModelDefinitionForm form);
        System.Collections.IList GetFilteredModelData(IModelDefinitionForm form, int? filterValue);
        object GetModelData(IModelDefinitionForm form, int Id);
        Tuple<object, System.Collections.IList> GetModelData(IMultiModelDefinitionForm form, int Id);
    }
}
