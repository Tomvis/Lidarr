import _ from 'lodash';
import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import EnhancedSelectInputSelectedValue from './EnhancedSelectInputSelectedValue';
import styles from './AlbumReleaseSelectInputSelectedValue.css';

// Tomvis fork (MUSIC-68): one row per monitored edition, so none is clipped
function AlbumReleaseSelectInputSelectedValue(props) {
  const {
    value,
    values,
    ...otherProps
  } = props;

  const valuesMap = _.keyBy(values, 'key');

  return (
    <EnhancedSelectInputSelectedValue
      className={styles.selectedValue}
      {...otherProps}
    >
      {
        value.map((key) => (
          <Label
            key={key}
            className={styles.label}
          >
            {valuesMap[key] ? valuesMap[key].value : key}
          </Label>
        ))
      }
    </EnhancedSelectInputSelectedValue>
  );
}

AlbumReleaseSelectInputSelectedValue.propTypes = {
  value: PropTypes.arrayOf(PropTypes.string).isRequired,
  values: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default AlbumReleaseSelectInputSelectedValue;
