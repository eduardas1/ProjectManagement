(function (app) {
    'use strict';
    function escaped(text, position) {
        var count = 0;
        while (position > 0 && text[--position] === '\\') count++;
        return count % 2 === 1;
    }
    function escapeValue(value) { return value.replace(/[\\(),|/^$=!<>]/g, '\\$&'); }
    function unescapeValue(value) { return value.replace(/\\(.)/g, '$1'); }
    app.projectSearchCompletions = function (text, position, grammar) {
        if (position < 0 || position > text.length || text.length > 2000) return null;
        var start = 0, end = text.length;
        for (var i = 0; i < text.length; i++) {
            if (',|()'.indexOf(text[i]) === -1 || escaped(text, i)) continue;
            if (i < position) start = i + 1;
            else { end = i; break; }
        }
        var segment = text.slice(start, end), cursor = position - start;
        var match = /^\s*([A-Za-z]*)/.exec(segment);
        var fieldStart = match[0].length - match[1].length, fieldEnd = match[0].length;
        var field = grammar.fields.find(function (candidate) { return candidate.name === match[1]; });
        function result(from, to, options) { return options.length ? {from: start + from, to: start + to, options: options} : null; }
        function option(label, apply, type, detail) { return {label: label, insert: apply, type: type || 'constant', detail: detail || ''}; }
        if (cursor < fieldStart) return null;
        if (cursor < fieldEnd || !field) {
            if (cursor > fieldEnd) return null;
            var prefix = segment.slice(fieldStart, cursor).toLowerCase();
            return result(fieldStart, fieldEnd, grammar.fields.filter(function (candidate) { return candidate.name.indexOf(prefix) === 0; })
                .map(function (candidate) { return option(candidate.name, candidate.name, 'property', candidate.kind); }));
        }
        var rest = segment.slice(fieldEnd), spaces = /^\s*/.exec(rest)[0].length;
        var operatorStart = fieldEnd + spaces;
        var operator = /^[=!<>*^$]*/.exec(segment.slice(operatorStart))[0];
        if (segment[operatorStart] === '!') operator = /^![=*^$]?/.exec(segment.slice(operatorStart))[0];
        var operatorEnd = operatorStart + operator.length;
        if (!operator || field.operators.indexOf(operator) === -1 || cursor < operatorEnd) {
            if (cursor > operatorEnd) return null;
            var operatorPrefix = segment.slice(operatorStart, cursor);
            return result(operatorStart, operatorEnd, field.operators.filter(function (candidate) { return candidate.indexOf(operatorPrefix) === 0; })
                .map(function (candidate) { return option(candidate, candidate, 'operator'); }));
        }
        if (cursor < operatorEnd) return null;
        var value = segment.slice(operatorEnd), valuePrefix = unescapeValue(segment.slice(operatorEnd, cursor));
        if (valuePrefix.endsWith('\\') && !escaped(valuePrefix, valuePrefix.length - 1)) return null;
        var choices = [];
        if (field.kind === 'enum') choices = field.values.map(function (value) { return option(value, value, 'enum'); });
        else if (field.kind === 'account') {
            choices = grammar.members.map(function (member) { return option(member.label, escapeValue(member.id), 'variable', 'Account ID'); });
            if (operator === '=' || operator === '!=') choices.unshift(option('No assignee', '', 'constant', 'Empty account'));
        } else if (field.kind === 'label') choices = grammar.labels.map(function (label) { return option(label, escapeValue(label), 'constant', 'Label'); });
        else if (field.kind === 'date' && (operator === '=' || operator === '!=')) choices = [option('No due date', '', 'constant', 'Empty date')];
        var prefix = valuePrefix.toLowerCase();
        choices = choices.filter(function (choice) { return !prefix || choice.label.toLowerCase().indexOf(prefix) !== -1 || unescapeValue(choice.insert).toLowerCase().indexOf(prefix) === 0; });
        return result(operatorEnd, operatorEnd + value.length, choices);
    };
})(window);
